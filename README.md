# Kkts.Expressions
Build string expression to lambda expression to support dynamic query from UI

get via nuget **[Kkts.Expressions](https://www.nuget.org/packages/Kkts.Expressions)** 

### Parser performance
Synchronous and asynchronous predicate parsing reuse per-call candidate buffers
and build token chains directly in lists. Each precedence pass uses read/write
cursors to replace consumed operands with their operator's built node, then
trims the unused tail once. Parser entries are never marked as consumed with
`null`, and long operator chains do not require repeated list shifts. This
avoids per-character candidate-list allocations and intermediate collections
without changing operator precedence, validation, or variable resolution.
Buffers are local to each parse; expressions and resolved variable values are
not cached across calls.

### Sample class
``` csharp
class Data
{
  public int Id { get; set; }
  public string Name { get; set; }
  public bool IsEnabled { get; set; }
  public DateTime CreationDate { get; set; }
  // ...
}

public class TestDbContext : DbContext
{
    // ...
    public DbSet<Data> Entities { get; set; }
    // ...
}
```
### Usage 1
``` csharp
// The property name is case insensitive for example id and Id are the same
EvaluationResult<Data, bool> evaluationResult = Interpreter.ParsePredicate<Data>("id = 1 and name='Test'");
// Use EvaluationResult to get validation result
// Should check if evaluationResult.Succeeded
Expression<Func<Data, bool>> predicate = evaluationResult.Result;

// Equivalent
var filters = new Filter[]
            {
                new Filter{ Property = "id", Operator = "=", Value = "1" },
                // and
                new Filter{ Property = "name", Operator = "=", Value = "Test" }
            };

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();

// Or use filters.TryBuildPredicate<Data>() to get validation result
```
### Usage 2
``` csharp
// The property name is case insensitive for example id and Id are the same
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("(id = 1 and name='Test1') or (id = 2 and name='Test2')").Result; 

// Equivalent
var filters = new FilterGroup[]
            {
                new FilterGroup
                {
                    Filters = new List<Filter>
                    {
                        new Filter{ Property = "id", Operator = "=", Value = "1" },
                        // and
                        new Filter{ Property = "name", Operator = "=", Value = "Test1" }
                    }
                },
                // or 
                new FilterGroup
                {
                    Filters = new List<Filter>
                    {
                        new Filter{ Property = "id", Operator = "=", Value = "2" },
                        // and
                        new Filter{ Property = "name", Operator = "=", Value = "Test2" }
                    }
                }
            }

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
// Or use filters.TryBuildPredicate<Data>() to get validation result
```

### Usage 3 (Order By)
``` csharp
// The property name is case insensitive for example id and Id are the same
var context = new TestDbContext();
// Order by Id then by Name descending (the direction is empty or asc or desc, the empty is the same asc)
var ordered = context.Entities.OrderBy("Id, Name desc"); 

// Equivalent
var ordered = context.Entities.OrderBy(new[] 
            {
              { new OrderByInfo { Property = "Id" },
              { new OrderByInfo { Property = "Name", Descending = true }
            }

// or
var evaluationResult = context.Entities.TryOrderBy(...);
if (evaluationResult.Succeeded) var ordered = evaluationResult.Result;
```
### Usage 4 (Variables) - V2 introduces Query Variables [Examples](https://github.com/linhnle/Kkts.Expressions/blob/main/examples/Kkts.Examples/Kkts.Examples.VariableResolver/Program.cs)
``` csharp
// It has 2 default variables: now, utcnow (they are case insensitive)
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("CreationDate = now or CreationDate = utcnow").Result; 
// Equivalent in c#
Expression<Func<Data, bool>> predicate = p => p.CreationDate == DateTime.Now || p.CreationDate == DateTime.UtcNow;

// We can compare the year, month, day, ... like 
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("CreationDate.year = now.year").Result;
// From V2 It is applied Query Variables by adding prefix $ before a variable 
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("CreationDate.year = $now.year").Result;
```
### Usage 5 (Custom Variables) - V2 introduces 2 new ways to declare variables [Examples](https://github.com/linhnle/Kkts.Expressions/blob/main/examples/Kkts.Examples/Kkts.Examples.VariableResolver/CustomVariableResolver.cs)
#### V2 introduces ParsePredicateAsync
``` csharp
class UserInfo
{
    public string UserName { get; set; }
    public int Id { get; set; }
}

class CustomVariableResolver : VariableResolver
{
    public UserInfo User { get; } = new UserInfo { Id = 1, UserName = "linhle" };
}

// Now it has new variables user.id and user.username
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("name = user.username and id = user.id", variableResolver: new CustomVariableResolver()).Result;

// V2 introduces Async
EvaluationResult<T, bool> result = await Interpreter.ParsePredicateAsync<Data>("name = user.username and id = user.id", variableResolver: new CustomVariableResolver()); 
```
### Usage 5 (Valid Properties)
``` csharp
// Only accept Id in predicate, if other properties occurs in predicate, an exception will be thrown
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("id = 1 and name='Test'", validProperties: new[] { "Id" }).Result; // throw an exception
```
### Usage 6 (Condition)
``` csharp
// The options can be deserialized from JSON
var options = new ConditionOptions 
{ 
  OrderBy = "...",
  OrderBys = new[] { new OrderByInfo { ... } },
  Filters = new[] { new Filter { ... } },
  FilterGroups = new[] { new FilterGroup { Filters = new[] { ... } } },
  Where = "..."
}
var condition = options.BuildCondition<Data>();
var context = new TestDbContext();
// Should check if condition.IsValid before calling where
var result = context.Entities.Where(condition).ToList();
// or paging
var result = context.Entities.Take(condition, new Pagination { Offset = 10, Limit = 10 });
var result = context.Entities.Take(condition, new Pagination { Page = 2, PageSize = 10 });
// or paging with total records count
var result = context.Entities.TakePage(condition, new Pagination { Offset = 10, Limit = 10 });
var result = context.Entities.TakePage(condition, new Pagination { Page = 2, PageSize = 10 });
```
### Usage 7 (Property Mapping)
``` csharp
// map entityId as Id
var mapping = new Dictionary<string, string>
            {
                ["entityId"] = "Id"
            };
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate<Data>("(entityId = 1 and name='Test1') or (entityId = 2 and name='Test2')", propertyMapping: mapping).Result; 

// Equivalent
var filters = new FilterGroup[]
            {
                new FilterGroup
                {
                    Filters = new List<Filter>
                    {
                        new Filter{ Property = "entityId", Operator = "=", Value = "1" },
                        // and
                        new Filter{ Property = "name", Operator = "=", Value = "Test1" }
                    }
                },
                // or 
                new FilterGroup
                {
                    Filters = new List<Filter>
                    {
                        new Filter{ Property = "entityId", Operator = "=", Value = "2" },
                        // and
                        new Filter{ Property = "name", Operator = "=", Value = "Test2" }
                    }
                }
            }
Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>(propertyMapping: mapping);
```
### Usage 8 (Applies to V2 - Query Variables in In Operator) [Examples](https://github.com/linhnle/Kkts.Expressions/blob/main/examples/Kkts.Examples/Kkts.Examples.VariablesAndInOperator/Program.cs)


### Support operators
| Operator             | Usage|Support data types|
|--------------------|--------------------------------------------|-------------------------------------------------------------------|
|Equals| Id = 1 or Id == 1 | Number, String, Guid, Boolean, DateTime, DateTimeOffset, Enum, Nullable |
|Not Equals| Id != 1 or Id <> 1 | Number, String, Guid, Boolean, DateTime, DateTimeOffset, Enum, Nullable |
|Less than| Id < 1 | Number, DateTime, DateTimeOffset, Nullable|
|Less than or Equal| Id <= 1 | Number, DateTime, DateTimeOffset, Nullable |
|Greater than| Id > 1 | Number, DateTime, DateTimeOffset, Nullable |
|Greater than or Equal| Id >= 1 | Number, DateTime, DateTimeOffset, Nullable |
|In| Id in [1, 2, 3, 4] or Name in ['String1', 'String2'] | Number, String, Guid, DateTime, DateTimeOffset, Enum, Nullable |
|Contains | Name.contains('Text') or Name @ 'Text' | String |
|Starts with | Name.startsWith('Text') or Name @* 'Text' | String |
|Ends with | Name.endsWith('Text') or Name \*@ 'Text' | String |
|Not | !IsEnabled or not(IsEnabled) or not(Id = 1) or !(Id = 1) | Boolean |
|Logical and (and or &&) | Id = 1 and Name = "Text" | Boolean |
|Logical or (or or \|\|) | Id = 1 or Name = "Text" | Boolean |
|Plus| Id + 1 > 5 or Name + '!' = 'Test!' | Number, nullable number, String (including mixed operands) |

Numeric literals in predicates use invariant culture and a period as the decimal
separator (for example, `8.3`), regardless of the current culture. When building
queries with interpolated numeric values, use `FormattableString.Invariant`.
Commas separate elements in `in` arrays; they are not decimal separators.

### Binary plus in predicates

Binary `+` supports property + property, property + value, and property + variable,
as well as literal expressions and parenthesized values:

``` csharp
var numeric = Interpreter.ParsePredicate<Data>("Id + 1 = 5");
var grouped = Interpreter.ParsePredicate<Data>("(Id + 1) = (2 + 3)");
var concatenated = Interpreter.ParsePredicate<Data>("Name + '!' = 'Test!'");
var mixed = Interpreter.ParsePredicate<Data>("'ID: ' + Id = 'ID: 7'");
var propertyPair = Interpreter.ParsePredicate<Data>("Id + Id = 8");

var variables = new VariableResolver();
variables.TryAdd("increment", 2);
variables.TryAdd("target", 6);
var variableSum = await Interpreter.ParsePredicateAsync<Data>(
    "Id + $increment = $target", variableResolver: variables);

var condition = new ConditionOptions { Where = "Id + 1 = 5" }.BuildCondition<Data>();
```

- Additions associate left to right, before comparisons and logical operators.
  Parentheses override grouping: `1 + 2 + 'x'` produces `"3x"`, while
  `1 + (2 + 'x')` produces `"12x"`.
- Numeric operands follow C# numeric promotion, including small integer promotion
  to `int` and mixed integer/floating-point sums. Whole-number addition literals
  use the first fitting `int`, `uint`, `long`, or `ulong`; fractional literals use
  `double`. Decimal operands cannot be mixed with `float` or `double`.
- Nullable numeric addition propagates null rather than replacing it with zero.
  For example, `NullableId + 1 = null` is true when `NullableId` is null.
  Integral overflow wraps as in unchecked C# addition; decimal overflow still
  throws when the predicate is evaluated.
- If either operand is a string, `+` concatenates, converting other operands
  using ordinary .NET/current-culture formatting. Null contributes an empty
  string. Quoted numeric text stays a string: `'1' + 2` produces `"12"`.
  Plus characters inside quoted strings are literal text.
- Computed strings work in infix comparisons and existing string-function
  arguments, such as `Name + '!' contains 'Test!'` and
  `Name.contains('Te' + 'st')`.
- Both synchronous and asynchronous predicate APIs, including runtime-type
  overloads and condition `Where`, support plus. Existing property mappings,
  allowlists, and variable diagnostics still apply to both operands.
- A predicate must return Boolean: `Id + 1` alone is not a predicate. Always
  check `Succeeded` and `Exception`, or `IsValid` and `Error` for a condition.
  Missing operands and unsupported operand pairs fail explicitly.
- Unary plus, other arithmetic operators, date/time arithmetic, user-defined
  addition, additions inside `in` array literals, and computed ordering are not
  supported. No new numeric signs, exponent notation, or suffix syntax is added.
  Native expression trees are emitted, but relational query translation,
  especially mixed string concatenation, depends on the provider.

## Contacts
**[LinkedIn](https://www.linkedin.com/in/linh-le-258417105/)**
**Skype: linh.nhat.le**
