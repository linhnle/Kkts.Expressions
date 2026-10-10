import { test } from "node:test";
import assert from "node:assert/strict";

const url = `${process.env.SAMPLE_URL ?? "http://127.0.0.1:5178"}/api/completion`;
async function request(text, offset = text.length, snapshot = 1) {
  return fetch(url, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text, offset, snapshot }),
  });
}

test("configured field, value, variable and legacy path responses apply exactly", async () => {
  for (const [text, offset, label, expected] of [
    ["StaTus = 'Active'", 3, "Status", "Status = 'Active'"],
    ["Status = 'AcZZ' and Price > 1", 12, "'Active'", "Status = 'Active' and Price > 1"],
    ["CreatedAt > $uZZ", 14, "utcnow", "CreatedAt > $utcnow"],
    ["Customer.NaZZ = 'A'", 11, "Customer.Name", "Customer.Name = 'A'"],
  ]) {
    const response = await request(text, offset, 42);
    assert.equal(response.status, 200);
    const data = await response.json();
    assert.equal(data.snapshot, 42);
    assert.equal(data.text, text);
    assert.equal(data.offset, offset);
    assert.equal(data.status, "Available");
    const item = data.items.find(candidate => candidate.label === label);
    assert.ok(item, JSON.stringify(data));
    assert.equal(text.slice(0, item.start) + item.insertionText + text.slice(item.start + item.length), expected);
    assert.equal(typeof item.description, "string");
    assert.equal(typeof item.kind, "string");
    assert.equal(JSON.stringify(data).includes("selector"), false);
  }
  const data = await (await request("Price ")).json();
  assert.deepEqual(data.items.map(item => item.insertionText), ["=", "!=", ">", ">=", "<", "<=", "in"]);
});

test("invalid arguments return Problem Details and over-budget source returns a limit", async () => {
  for (const [text, offset] of [[null, 0], ["Price", -1], ["Price", 6], ["😀", 1]]) {
    const response = await request(text, offset);
    assert.equal(response.status, 400);
    assert.match(response.headers.get("content-type"), /application\/problem\+json/);
  }
  const data = await (await request(" ".repeat(4097))).json();
  assert.equal(data.status, "LimitExceeded");
  assert.equal(data.items.length, 0);
  assert.equal(data.isIncomplete, true);
});

test("diagnostics match the exact CRLF snapshot including EOF carets", async () => {
  const text = "Status = '😀'\r\nand Price > ";
  const data = await (await request(text)).json();
  assert.equal(data.text, text);
  assert.ok(data.diagnostics.some(item => item.start === text.length && item.length === 0));
  assert.ok(data.diagnostics.every(item => item.start >= 0 && item.start + item.length <= text.length));
});
