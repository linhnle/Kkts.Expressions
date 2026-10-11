import { test } from "node:test";
import assert from "node:assert/strict";
import { EditorState } from "@codemirror/state";
import { SnapshotRequests, applyItem, completionOptions, lintDiagnostics } from "../adapter.js";

function fixture(t) {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  let snapshot = { text: "Status = ", offset: 9 };
  const pending = [];
  const published = [];
  const errors = [];
  const requests = new SnapshotRequests({
    readSnapshot: () => snapshot,
    publish: record => published.push(record),
    reportError: error => errors.push(error),
    fetchResponse: (_url, options) => new Promise(resolve => {
      pending.push({ options, resolve });
    }),
  });
  const response = (record, items = [], diagnostics = []) => ({
    ok: true,
    json: async () => ({ text: record.text, offset: record.offset, snapshot: record.snapshot, items, diagnostics }),
  });
  return {
    requests, pending, published, errors, response,
    move: next => { snapshot = next; return requests.request(next); },
    start: () => requests.request(snapshot),
  };
}

test("debounces 150 ms and cancels superseded typing snapshots", async t => {
  const f = fixture(t);
  const first = f.start();
  t.mock.timers.tick(149);
  assert.equal(f.pending.length, 0);
  const second = f.move({ text: "Status = A", offset: 10 });
  assert.equal(await first.promise, null);
  t.mock.timers.tick(149);
  assert.equal(f.pending.length, 0);
  t.mock.timers.tick(1);
  assert.equal(f.pending.length, 1);
  assert.deepEqual(JSON.parse(f.pending[0].options.body),
    { text: second.text, offset: second.offset, snapshot: second.snapshot });
  f.pending[0].resolve(f.response(second));
  assert.equal(await second.promise, second);
  assert.deepEqual(f.published, [second]);
});

test("out-of-order responses and cursor-only changes cannot publish stale diagnostics", async t => {
  const f = fixture(t);
  const first = f.start();
  t.mock.timers.tick(150);
  const second = f.move({ text: first.text, offset: 3 });
  assert.equal(f.pending[0].options.signal.aborted, true);
  t.mock.timers.tick(150);
  f.pending[1].resolve(f.response(second));
  await second.promise;
  f.pending[0].resolve(f.response(first, [], [{ start: 0, length: 1, message: "stale" }]));
  await Promise.resolve();
  await Promise.resolve();
  assert.deepEqual(f.published, [second]);
  assert.equal(f.requests.isCurrent(first), false);
  assert.equal(await first.promise, null);
});

test("response identity must match document, cursor and monotonic request id", async t => {
  const f = fixture(t);
  const record = f.start();
  t.mock.timers.tick(150);
  f.pending[0].resolve({ ok: true, json: async () => ({
    text: record.text, offset: record.offset, snapshot: record.snapshot + 1, items: [], diagnostics: [],
  }) });
  assert.equal(await record.promise, null);
  assert.equal(f.published.length, 0);
});

test("exact per-item application preserves middle edits and rejects stale acceptance", async t => {
  const f = fixture(t);
  const record = f.move({ text: "StaTus = 'Active'", offset: 3 });
  t.mock.timers.tick(150);
  const item = { kind: "Field", label: "Status", insertionText: "Status", start: 0, length: 6,
    description: "<b>render as text</b>", type: { name: "String" } };
  f.pending[0].resolve(f.response(record, [item]));
  await record.promise;
  let edited;
  const view = {
    state: { doc: { toString: () => record.text }, selection: { main: { head: record.offset } } },
    dispatch: transaction => {
      const edit = transaction.changes;
      edited = record.text.slice(0, edit.from) + edit.insert + record.text.slice(edit.to);
    },
  };
  const option = completionOptions(view, f.requests, record)[0];
  assert.equal(option.type, "property");
  assert.equal(option.info, "<b>render as text</b>");
  assert.equal(option.apply(), true);
  assert.equal(edited, "Status = 'Active'");
  f.move({ text: record.text, offset: 0 });
  edited = undefined;
  assert.equal(applyItem(view, f.requests, record, item), false);
  assert.equal(edited, undefined);
});

test("CRLF, supplementary characters and zero-length EOF diagnostics use UTF-16", () => {
  const text = "Status = '😀'\r\nand Price > ";
  const record = { text, data: { diagnostics: [
    { code: "missing-operand", message: "Expected operand.", start: text.length, length: 0 },
    { code: "completion-context-unavailable", message: "Unavailable.", start: 10, length: 2 },
  ] } };
  const diagnostics = lintDiagnostics(record);
  assert.deepEqual([diagnostics[0].from, diagnostics[0].to], [text.length, text.length]);
  assert.equal(diagnostics[1].severity, "info");
  const secondLineStart = text.indexOf("\n") + 1;
  assert.equal(secondLineStart + 4, text.indexOf("Price"));
  assert.equal(text.slice(10, 12), "😀");
  assert.throws(() => lintDiagnostics({ text: "", data: { diagnostics: [{ start: 1, length: 0 }] } }), RangeError);
});

test("HTTP errors are visible rather than converted to empty successful suggestions", async t => {
  const f = fixture(t);
  const record = f.start();
  t.mock.timers.tick(150);
  f.pending[0].resolve({ ok: false, status: 400 });
  assert.equal(await record.promise, null);
  assert.equal(f.errors.length, 1);
  assert.match(f.errors[0].message, /400/);
  assert.equal(f.published.length, 0);
});

test("CodeMirror line coordinates and exact request serialization use the same document", async t => {
  const f = fixture(t);
  const state = EditorState.create({ doc: "Status = '😀'\r\nand Price > " });
  const text = state.doc.toString();
  assert.equal(state.doc.line(2).from + 4, text.indexOf("Price"));
  const rawText = "Status = '😀'\r\nand Price > ";
  const record = f.move({ text: rawText, offset: rawText.length });
  t.mock.timers.tick(150);
  assert.equal(JSON.parse(f.pending[0].options.body).text, rawText);
  f.pending[0].resolve(f.response(record));
  await record.promise;
});
