# opensearch-query-translation Specification

## Purpose

Let applications reuse Kkts.Expressions predicates, ordering, and pagination against OpenSearch by translating them into equivalent OpenSearch Query DSL, failing explicitly whenever an equivalent query cannot be produced.

## Requirements

### Requirement: Separate client-independent translation package
The system SHALL provide a `Kkts.Expressions.OpenSearch` package targeting `netstandard2.0` that depends only on `Kkts.Expressions` and `System.Text.Json`. It SHALL translate any `Expression<Func<T, bool>>` or single-parameter Boolean `LambdaExpression`, regardless of whether it came from `Interpreter`, structured filters, filter groups, filter trees, `ExpressionQueryContext`, a `Condition<T>`, or application code. Translation MUST NOT compile or execute the predicate against data, MUST NOT contact OpenSearch, MUST NOT modify the input, and MUST NOT apply, relax, or re-evaluate query policies; policies remain enforced when the predicate is built. Output SHALL be OpenSearch Query DSL represented as `System.Text.Json` nodes and serializable to a JSON string, with deterministic property and clause order for the same input and configuration.

#### Scenario: Translate an interpreted predicate
- **WHEN** `"id = 1 and name = 'Test'"` is parsed with `Interpreter.ParsePredicate<Data>` and the result is translated with default options
- **THEN** translation succeeds with a `bool` query whose `filter` clauses are `{"term":{"id":1}}` and `{"term":{"name.keyword":"Test"}}`

#### Scenario: Same predicate from different entry points
- **WHEN** equivalent predicates are built from a string, a structured `Filter` list, and a `FilterNode` tree
- **THEN** their translated JSON strings are identical

#### Scenario: Deterministic output
- **WHEN** the same predicate is translated twice with the same configuration
- **THEN** the serialized JSON is byte-for-byte identical

### Requirement: All-or-nothing results with diagnostics
Translation SHALL return a result reporting success, the translated output on success, and an ordered list of diagnostics on failure. Each diagnostic MUST include a stable code, a human-readable message, and when applicable the CLR member path and the text of the offending sub-expression. A failed translation MUST NOT expose a partial query. A throwing convenience API SHALL raise an exception that carries the same diagnostics. Diagnostic messages MUST NOT embed captured variable values.

#### Scenario: Partial failure
- **WHEN** a predicate `Id = 1 and Price + 1 > 5` is translated
- **THEN** translation fails, no query is returned, and a diagnostic identifies the arithmetic sub-expression

#### Scenario: Throwing API
- **WHEN** the throwing API translates an unsupported predicate
- **THEN** it throws an exception whose diagnostics equal those of the non-throwing result

### Requirement: Logical composition
Conjunctions SHALL translate to `bool.filter`, disjunctions to `bool.should` with `minimum_should_match` of 1, and negation to `bool.must_not`. Directly nested conjunctions and directly nested disjunctions SHALL be flattened into one `bool` clause. The top-level query SHALL always be a `bool` query that places the translated clauses in `filter` context so results are not relevance-scored. A constant `true` predicate SHALL translate to `match_all` and a constant `false` predicate to `match_none`.

#### Scenario: Mixed logic
- **WHEN** `(a = 1 or b = 2) and not(c = 3)` is translated
- **THEN** the result is a `bool.filter` containing a `bool.should` of the two term clauses with `minimum_should_match` 1 and a `bool.must_not` of the `c` term clause

#### Scenario: Flattening
- **WHEN** `a = 1 and b = 2 and c = 3` is translated
- **THEN** the three term clauses appear in a single `filter` array

#### Scenario: Constant predicates
- **WHEN** `x => true` and `x => false` are translated
- **THEN** their query clauses are `match_all` and `match_none` respectively

### Requirement: Comparison translation with LINQ null semantics
Equality SHALL translate to `term`; inequality to `must_not` of `term`; `<`, `<=`, `>`, `>=` to `range` with `lt`, `lte`, `gt`, `gte`. Comparisons with the constant operand on the left SHALL be normalized so the field is the subject, mirroring the operator. Equality with `null` SHALL translate to `must_not` of `exists`, and inequality with `null` to `exists`. A document missing a field SHALL be treated as a null value so that results match LINQ-to-Objects semantics: inequality with a non-null value and negated predicates match missing values; equality and range comparisons do not.

#### Scenario: Inequality matches null
- **WHEN** `NullableInteger != 5` is translated and executed against documents with values 5, 6, and a missing value
- **THEN** the documents with 6 and the missing value match, as in LINQ

#### Scenario: Reversed operands
- **WHEN** a predicate `x => 5 < x.Age` is translated
- **THEN** the result is `{"range":{"age":{"gt":5}}}`

#### Scenario: Null equality
- **WHEN** `NullableInteger = null` is translated
- **THEN** the result is a `must_not` of `{"exists":{"field":"nullableInteger"}}`

#### Scenario: Range excludes missing values
- **WHEN** `NullableInteger > 1` is executed against a document missing that field
- **THEN** the document does not match

### Requirement: Membership translation
For scalar fields, `in` SHALL translate to `terms` over the non-null, de-duplicated list values in first-seen order; `not in` SHALL translate to `must_not` of the equivalent `in` clause. A null list element SHALL add a disjunct matching missing values. Conversion-aware operands SHALL instead use equivalent numeric rewrites when required to preserve comparison semantics. An empty list SHALL translate `in` to `match_none` and `not in` to `match_all`. Lists supplied through constant arrays, captured variables, and collection variables SHALL be supported. These scalar-field null rules MUST NOT be applied to null-element membership on flattened collections.

#### Scenario: Basic membership
- **WHEN** `Id in [1, 2, 2]` is translated
- **THEN** the result is `{"terms":{"id":[1,2]}}`

#### Scenario: Null element
- **WHEN** `NullableInteger in [null, 1]` is translated
- **THEN** the result matches documents whose value is 1 or whose field is missing

#### Scenario: Not in with nulls
- **WHEN** `NullableInteger not in [1, 2]` and `NullableInteger not in [null, 1]` are executed against a document missing the field
- **THEN** the first matches and the second does not, as in LINQ

#### Scenario: Empty list
- **WHEN** `Id in []` and `Id not in []` are translated
- **THEN** they produce `match_none` and `match_all` respectively

### Requirement: String function translation
`StartsWith` SHALL translate to `prefix`; `Contains` to `wildcard` with value `*text*`; `EndsWith` to `wildcard` with value `*text`. Wildcard metacharacters `*`, `?`, and `\` in the operand MUST be escaped so they match literally. An empty operand SHALL translate to `exists` on the field. String functions, equality, and membership on strings SHALL be case-sensitive by default and SHALL use OpenSearch case-insensitive matching only for fields explicitly configured as case-insensitive. A null operand MUST fail translation.

#### Scenario: Escaped wildcard
- **WHEN** `Name.contains('50%*off?')` is translated
- **THEN** the wildcard value is `*50%\*off\?*` targeting the field's keyword form

#### Scenario: Case-insensitive field
- **WHEN** a field is configured as case-insensitive and `Name = 'abc'` is executed against a document with `ABC`
- **THEN** the term clause sets `case_insensitive` to true and the document matches

#### Scenario: Case-sensitive default
- **WHEN** `Name = 'abc'` is executed with default options against a document with `ABC`
- **THEN** the document does not match

### Requirement: Boolean member translation
A standalone Boolean member predicate SHALL translate to a `term` with value `true`, and its negation to `must_not` of that term. Explicit comparisons with Boolean constants SHALL translate as ordinary equality or inequality.

#### Scenario: Standalone and negated Boolean
- **WHEN** `IsEnabled` and `!IsEnabled` are translated
- **THEN** the results are `{"term":{"isEnabled":true}}` and a `must_not` containing that term

### Requirement: Value formatting
Constant and captured values SHALL be formatted as JSON values without using the current culture: integral and floating-point types and `decimal` as JSON numbers; `bool` as JSON Boolean; `string` and `char` as strings; `Guid` as lowercase hyphenated strings; `DateTime` and `DateTimeOffset` as ISO 8601 round-trip strings preserving UTC, offset, or unspecified kind; enums as their underlying numeric value by default or their names when configured globally or per field. `TimeSpan` and other types SHALL require an application-supplied value converter for the type or field and MUST fail translation otherwise. Non-finite floating-point values MUST fail translation. Values reachable only through parameter-free constant, captured-field, captured-property, conversion, and array-initialization expressions SHALL be evaluated during translation; other parameter-free computations MUST fail translation rather than be executed.

#### Scenario: Culture independence
- **WHEN** `Price > 8.3` is translated under a culture using a comma decimal separator
- **THEN** the range value is the JSON number `8.3`

#### Scenario: Enum names
- **WHEN** an enum field is configured to format names and `Status = 'Active'` is translated
- **THEN** the term value is the string `"Active"`

#### Scenario: TimeSpan without converter
- **WHEN** a `TimeSpan` comparison is translated without a configured converter
- **THEN** translation fails with a diagnostic naming the field and type

#### Scenario: Captured variable
- **WHEN** a predicate built with a built-in date variable or a C# closure value is translated
- **THEN** the captured value is formatted into the query

### Requirement: Field name mapping
Each member access chain rooted at the predicate parameter SHALL map to an OpenSearch field path by joining per-member names with `.`. A member's name SHALL be its `[JsonPropertyName]` value when present, otherwise the configured naming policy applied to the CLR name; the default policy is camelCase. Applications SHALL be able to override the field path for a member chain using a strongly typed selector, and the override SHALL replace the whole path. Member chains that cannot be mapped, such as computed selectors registered as public expression fields, MUST fail translation. Conversions that only lift nullability, widen numerics, or expose an enum's underlying type SHALL not change field identity, but the translator MUST retain any conversion information needed to preserve predicate semantics.

#### Scenario: Default camelCase paths
- **WHEN** `Customer.Address.City = 'Hanoi'` is translated with default options
- **THEN** the field is `customer.address.city.keyword`

#### Scenario: Attribute and override precedence
- **WHEN** a property has `[JsonPropertyName("full_name")]` and another member chain has a typed override to `meta.created`
- **THEN** the translated field paths are based on `full_name` and `meta.created` respectively

#### Scenario: Unmapped computed field
- **WHEN** a predicate on a public expression field whose selector concatenates two properties is translated
- **THEN** translation fails with a diagnostic that identifies the unmappable selector

### Requirement: Exact numeric conversion rewrites
Numeric widening conversions on field operands SHALL be supported when a DSL rewrite can preserve the original CLR comparison or membership semantics. The translator MUST NOT discard a conversion merely because C# classifies it as widening. For integral fields widened to `float` or `double`, equality, inequality, ordered comparisons, and membership SHALL account for rounding by translating the corresponding source-value intervals. Nullable operands MUST preserve the existing lifted-comparison and negation rules. When an exact rewrite cannot be established, translation MUST fail with a diagnostic rather than approximate the predicate or emit a script.

#### Scenario: Rounded integral equality
- **WHEN** `x => (double)x.LongValue == 9007199254740992d` is translated against an integral index field
- **THEN** the emitted query matches both source values 9007199254740992 and 9007199254740993, as the CLR predicate does

#### Scenario: Rounded range boundary
- **WHEN** `x => (double)x.LongValue > 9007199254740992d` is translated
- **THEN** source value 9007199254740993 does not match, and the boundary agrees with CLR evaluation

#### Scenario: Converted membership
- **WHEN** a constant floating-point list contains 9007199254740992d and membership tests `(double)x.LongValue`
- **THEN** both source values 9007199254740992 and 9007199254740993 match the translated query

#### Scenario: Unprovable conversion
- **WHEN** a conversion has no established equivalent DSL rewrite
- **THEN** translation fails with a conversion diagnostic and no query is returned

### Requirement: Keyword and text string fields
String fields SHALL have a configurable kind. The default SHALL match OpenSearch dynamic mapping: a `text` field with a `keyword` subfield named `keyword`. Fields SHALL be configurable as `keyword`, as `text` with a named keyword subfield, or as `text` without a keyword subfield, both globally and per field. Equality, membership, string functions, and sorting on strings SHALL target the keyword field or subfield. Using these operations on a text-only field MUST fail translation with a diagnostic rather than emit tokenized full-text matching.

#### Scenario: Keyword field
- **WHEN** a field is configured as `keyword` and `Code = 'A1'` is translated
- **THEN** the clause is `{"term":{"code":"A1"}}`

#### Scenario: Custom subfield
- **WHEN** a field is configured as text with subfield `raw` and `Name.startsWith('Al')` is translated
- **THEN** the clause is `{"prefix":{"name.raw":"Al"}}`

#### Scenario: Text-only field
- **WHEN** a text-only field is used with equality
- **THEN** translation fails with a diagnostic explaining that exact matching requires a keyword field

### Requirement: Nested documents
Applications SHALL be able to mark member paths as OpenSearch `nested` paths. A condition whose field lies under a nested path of a single-valued object SHALL be wrapped in a `nested` query for that path, with any negation applied outside the `nested` query. A call `collection.Any(element => predicate)` on a collection marked as nested SHALL translate to one `nested` query whose inner query is the translated element predicate, so all inner conditions apply to the same element. `collection.Any()` without a predicate SHALL translate to a `nested` query with `match_all`. Nested paths within other nested paths SHALL produce correspondingly nested `nested` queries with full field paths.

#### Scenario: Same-element matching
- **WHEN** `x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2)` is translated with `Lines` marked nested and executed against a document with lines `{A, 1}` and `{B, 5}`
- **THEN** the query is a single `nested` query on `lines` and the document does not match

#### Scenario: Negation outside nested
- **WHEN** `Customer.Name != 'a'` is translated with `Customer` marked nested
- **THEN** the clause is a `must_not` containing a `nested` query on `customer`

#### Scenario: Any without predicate
- **WHEN** `x => x.Lines.Any()` is translated with `Lines` marked nested
- **THEN** the clause is a `nested` query on `lines` with `match_all`

### Requirement: Non-nested collections
For collections not marked nested, `collection.Contains(nonNullValue)` on a scalar collection SHALL translate to `term` on the collection's field, and `collection.Any(element => predicate)` SHALL be supported only when the predicate is a single positive condition (equality with a non-null value, membership over exclusively non-null values, range, or string function) on the element or one of its members. `Contains(null)` and `Any` membership whose candidate list contains null MUST fail translation with a diagnostic because flattened arrays do not retain null-element presence. Other predicates over non-nested collections, including negations, inequality, null comparisons, and multiple conditions, MUST fail translation with a diagnostic recommending a nested mapping, because flattened arrays cannot preserve their LINQ semantics. This restriction SHALL NOT change scalar nullable-field membership or null checks on scalar fields within an explicitly nested element scope.

#### Scenario: Scalar array
- **WHEN** `x => x.Tags.Contains("red")` is translated with default options
- **THEN** the clause is `{"term":{"tags.keyword":"red"}}`

#### Scenario: Unsafe flattened predicate
- **WHEN** `x => x.Lines.Any(l => l.Sku == "A" && l.Quantity > 2)` is translated without marking `Lines` nested
- **THEN** translation fails with a diagnostic recommending a nested mapping

#### Scenario: Flattened null-element membership
- **WHEN** `x => x.Values.Contains(null)` or `x => x.Values.Any(v => new int?[] { null, 1 }.Contains(v))` is translated without a nested representation
- **THEN** translation fails with a flattened-collection diagnostic and no partial query, rather than treating missing indexed values as proof of a null element

### Requirement: Explicit ordinary-object presence markers
Applications SHALL be able to register a typed ordinary-object member selector with an explicit indexed Boolean presence-field path. The application MUST index `true` for a non-null object, including an empty object or an object with only null children, and `false` for a null object in every document in the relevant scope. Ordinary-object equality with null SHALL translate to a term matching the marker value `false`, and inequality with null to a term matching `true`. Marker paths SHALL be used as explicitly configured, without applying string keyword suffixes. Invalid selector or marker-path registrations MUST fail explicitly. Unconfigured ordinary-object null checks MUST fail translation, rather than infer presence from child fields. Presence markers SHALL NOT change the existing nested-object presence-query behavior, and the translator SHALL NOT create or populate markers or contact the index to verify them.

#### Scenario: Distinguish null from an empty object
- **WHEN** `x => x.Customer == null` is translated with `Customer` registered to Boolean marker `customerPresent`
- **THEN** the query matches `customerPresent: false` and does not match `customerPresent: true`, including documents whose customer is `{}` or has only null children

#### Scenario: Non-null ordinary object
- **WHEN** `x => x.Customer != null` is translated with the same registration
- **THEN** the query matches `customerPresent: true`

#### Scenario: No configured marker
- **WHEN** an ordinary-object null check has no presence-marker registration
- **THEN** translation fails with a presence-marker diagnostic and no partial output

#### Scenario: Indexing responsibility
- **WHEN** the presence-marker guide is followed
- **THEN** it states that every document must carry the correct Boolean marker and that missing or stale markers invalidate the equivalence guarantee

### Requirement: Unsupported constructs fail explicitly
Translation MUST fail with diagnostics, and MUST NOT fall back to script queries or approximations, for arithmetic, string concatenation, comparisons between two fields, method calls other than the supported string functions and collection operations, conditional expressions, type tests, parameters other than the root and supported collection elements, and any other node without a supported translation.

#### Scenario: Arithmetic
- **WHEN** `Id + 1 > 5` is translated
- **THEN** translation fails with an unsupported-expression diagnostic

#### Scenario: Field-to-field comparison
- **WHEN** `x => x.Start < x.End` is translated
- **THEN** translation fails with an unsupported-expression diagnostic

### Requirement: Public sort keys on ordering clauses
The core `OrderByClause` SHALL expose, for a given element type, its ordered sort keys, each with a key-selector lambda and a descending flag, in the same order and with the same field resolution used by its `Sort` methods. Exposing keys MUST NOT change existing sorting behavior or require compilation.

#### Scenario: Ordered keys
- **WHEN** `"name desc, id"` is parsed into an `OrderByClause` for `Data` and its keys are read
- **THEN** two keys are returned in order: `Name` descending and `Id` ascending

#### Scenario: Mapped field keys
- **WHEN** an ordering is built through a property mapping or a public expression field
- **THEN** each returned key selector is structurally equal to the selector its `Sort` methods apply

### Requirement: Sort translation
Ordering clauses SHALL translate to an OpenSearch `sort` array preserving key order, with `order` set to `asc` or `desc`. Keys SHALL use the same field mapping as predicates; string keys MUST target the keyword field or subfield; keys under a nested path SHALL include a `nested` option with that path. Missing values SHALL sort first in ascending and last in descending order, matching LINQ-to-Objects null ordering. Keys that are not mappable member chains, or that target text-only string fields, MUST fail translation.

#### Scenario: Multi-key sort
- **WHEN** `"name desc, id"` is translated
- **THEN** the sort is `[{"name.keyword":{"order":"desc","missing":"_last"}},{"id":{"order":"asc","missing":"_first"}}]`

#### Scenario: Computed sort key
- **WHEN** an ordering on a computed public expression field is translated
- **THEN** translation fails with a diagnostic

### Requirement: Pagination translation
`Pagination` SHALL translate to `from` equal to its `Offset` and `size` equal to its `Limit`. When `from + size` exceeds a configurable maximum result window, defaulting to 10,000, translation MUST fail with a diagnostic that reports the limit and the observed value.

#### Scenario: Page translation
- **WHEN** a `Pagination` with `Offset` 40 and `Limit` 20 is translated
- **THEN** `from` is 40 and `size` is 20

#### Scenario: Result window exceeded
- **WHEN** `from + size` would be 10,020 with the default maximum
- **THEN** translation fails with a result-window diagnostic

### Requirement: Search body composition
The system SHALL compose a search request body containing `query`, and optionally `sort`, `from`, and `size`, from a predicate or collection of predicates combined with AND, an optional ordering clause, and optional pagination. A `Condition<T>` SHALL be accepted directly, translating its predicates and ordering; an invalid condition MUST fail without translation. Diagnostics from all parts SHALL be reported together.

#### Scenario: Full body
- **WHEN** a valid `Condition<Data>` with two predicates and an ordering is translated with `Offset` 0 and `Limit` 10
- **THEN** the body contains a `query` with both predicates in `bool.filter`, the translated `sort`, `from` 0, and `size` 10

#### Scenario: Invalid condition
- **WHEN** a `Condition<Data>` whose `IsValid` is false is translated
- **THEN** translation fails with a diagnostic and no body is returned

### Requirement: OpenSearch.Client adapter
The system SHALL provide a separate `Kkts.Expressions.OpenSearch.Client` package depending on `Kkts.Expressions.OpenSearch` and `OpenSearch.Client` 2.x. It SHALL convert successful translations into an `OpenSearch.Client` query container, sort definitions, and `from`/`size`, and SHALL offer search-descriptor extensions that apply them. The adapter MUST produce request JSON equivalent to the raw translation and MUST reject failed translations with the translation diagnostics.

#### Scenario: Equivalent request
- **WHEN** a predicate, ordering, and pagination are applied to a `SearchDescriptor<Data>` through the adapter and the request is serialized by the client
- **THEN** its `query`, `sort`, `from`, and `size` are JSON-equivalent to the raw translated body

#### Scenario: Failed translation
- **WHEN** the adapter is given a failed translation
- **THEN** it throws with the translation diagnostics

### Requirement: Verified semantic equivalence and documentation
Supported translations SHALL be verified by executing translated queries against a real OpenSearch instance and comparing matched document identifiers and order with LINQ-to-Objects results for the same predicates, ordering, and pagination over the same seed data. The package SHALL ship a guide and README section describing installation, field mapping configuration, supported constructs, semantic differences, and diagnostics.

#### Scenario: Equivalence suite
- **WHEN** the integration suite runs with Docker available
- **THEN** every supported operator, null case, nested case, sort, and page case returns the same document identifiers as LINQ-to-Objects
