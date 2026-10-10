import { EditorState } from "@codemirror/state";
import { EditorView, keymap, ViewPlugin, lineNumbers } from "@codemirror/view";
import { defaultKeymap } from "@codemirror/commands";
import { autocompletion, completionKeymap } from "@codemirror/autocomplete";
import { lintGutter, setDiagnostics } from "@codemirror/lint";
import { SnapshotRequests, completionOptions, lintDiagnostics } from "./adapter.js";

const status = document.querySelector("#status");
let view;
const readSnapshot = () => ({
  text: view.state.doc.toString(), offset: view.state.selection.main.head,
});
const requests = new SnapshotRequests({
  readSnapshot,
  publish: record => {
    view.dispatch(setDiagnostics(view.state, lintDiagnostics(record)));
    status.textContent = `${record.data.status}${record.data.isIncomplete ? " (bounded subset)" : ""}\n` +
      record.data.diagnostics.map(item => item.message).join("\n");
  },
  reportError: error => {
    status.textContent = error.message;
    console.error(error);
  },
});

const snapshots = ViewPlugin.fromClass(class {
  update(update) {
    if (!update.docChanged && !update.selectionSet) return;
    status.textContent = "Waiting for the current snapshot...";
    const record = requests.request(readSnapshot());
    queueMicrotask(() => {
      if (requests.isCurrent(record)) view.dispatch(setDiagnostics(view.state, []));
    });
  }
  destroy() { requests.cancel(); }
});

view = new EditorView({
  parent: document.querySelector("#editor"),
  state: EditorState.create({
    doc: "Status = ",
    selection: { anchor: 9 },
    extensions: [
      lineNumbers(), lintGutter(), snapshots,
      keymap.of([...completionKeymap, ...defaultKeymap]),
      autocompletion({
        override: [async context => {
          const record = await requests.request(readSnapshot()).promise;
          if (!record || !requests.isCurrent(record) || context.aborted) return null;
          return { from: context.pos, filter: false, options: completionOptions(view, requests, record) };
        }],
      }),
    ],
  }),
});
requests.request(readSnapshot());
