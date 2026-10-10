const types = {
  Field: "property", Operator: "keyword", Value: "constant",
  Variable: "variable", LogicalOperator: "keyword", Delimiter: "keyword",
};

const sameSnapshot = (left, right) =>
  left.text === right.text && left.offset === right.offset;

export class SnapshotRequests {
  constructor({ readSnapshot, publish, reportError, fetchResponse = (...args) => fetch(...args) }) {
    this.readSnapshot = readSnapshot;
    this.publish = publish;
    this.reportError = reportError;
    this.fetchResponse = fetchResponse;
    this.sequence = 0;
    this.current = null;
  }

  request(snapshot) {
    if (this.current && sameSnapshot(this.current, snapshot)) return this.current;
    this.cancel();
    const record = { ...snapshot, snapshot: ++this.sequence, controller: new AbortController() };
    record.promise = new Promise(resolve => { record.resolve = resolve; });
    this.current = record;
    record.timer = setTimeout(() => this.send(record), 150);
    return record;
  }

  cancel() {
    if (!this.current) return;
    clearTimeout(this.current.timer);
    this.current.controller.abort();
    this.current.resolve(null);
    this.current = null;
  }

  isCurrent(record) {
    return this.current === record && sameSnapshot(record, this.readSnapshot());
  }

  async send(record) {
    try {
      const response = await this.fetchResponse("/api/completion", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: record.text, offset: record.offset, snapshot: record.snapshot }),
        signal: record.controller.signal,
      });
      if (!response.ok) throw new Error(`Completion request failed (${response.status}).`);
      const data = await response.json();
      if (!this.isCurrent(record) || data.snapshot !== record.snapshot || !sameSnapshot(data, record)) {
        record.resolve(null);
        return;
      }
      record.data = data;
      this.publish(record);
      record.resolve(record);
    } catch (error) {
      if (error.name !== "AbortError" && this.isCurrent(record)) this.reportError(error);
      record.resolve(null);
    }
  }
}

export function lintDiagnostics(record) {
  return record.data.diagnostics.map(item => {
    if (item.start < 0 || item.length < 0 || item.start + item.length > record.text.length)
      throw new RangeError("Backend diagnostic is outside its source snapshot.");
    return {
      from: item.start, to: item.start + item.length, message: item.message,
      severity: item.code === "completion-context-unavailable" ? "info" :
        item.code.endsWith("diagnostics-truncated") ? "warning" : "error",
    };
  });
}

export function applyItem(view, requests, record, item) {
  if (!requests.isCurrent(record) || view.state.doc.toString() !== record.text ||
      view.state.selection.main.head !== record.offset) return false;
  if (item.start < 0 || item.length < 0 || item.start + item.length > record.text.length)
    throw new RangeError("Backend completion is outside its source snapshot.");
  view.dispatch({
    changes: { from: item.start, to: item.start + item.length, insert: item.insertionText },
    selection: { anchor: item.start + item.insertionText.length },
  });
  return true;
}

export function completionOptions(view, requests, record) {
  return record.data.items.map(item => ({
    label: item.label, detail: item.type?.name, info: item.description,
    type: types[item.kind],
    apply: () => applyItem(view, requests, record, item),
  }));
}
