# Kkts.Expressions
Build string expression to lambda expression to support dynamic query from UI

get via nuget **[Kkts.Expressions](https://www.nuget.org/packages/Kkts.Expressions)** 
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


## Contacts
**[LinkedIn](https://www.linkedin.com/in/linh-le-258417105/)**
**Skype: linh.nhat.le**
