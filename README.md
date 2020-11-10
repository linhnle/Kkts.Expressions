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
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("id = 1 and name='Test'"); 

// Equivalent
var filters = new Filter[]
            {
                new Filter{ Property = "id", Operator = "=", Value = "1" },
                // 
                new Filter{ Property = "name", Operator = "=", Value = "Test" }
            };

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
```
### Usage 2
``` csharp
// The property name is case insensitive for example id and Id are the same
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("(id = 1 and name='Test1') or (id = 2 and name='Test2')"); 

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
                        new Filter{ Property = "id", Operator = "=", Value = "1" },
                        // and
                        new Filter{ Property = "name", Operator = "=", Value = "Test2" }
                    }
                }
            }

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
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

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
```
### Usage 4 (Variables)
``` csharp
// It has 2 default variables: now, utcnow (they are case insensitive)
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("CreationDate = now or CreationDate = utcnow"); 
// Equivalent in c#
Expression<Func<Data, bool>> predicate = p => p.CreationDate == DateTime.Now || p.CreationDate == DateTime.UtcNow;

// We can compare the year, month, day, ... like 
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("CreationDate.year = now.year")
```
### Usage 5 (Custom Variables)
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
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("name = user.username and id = user.id", variableResolver: new CustomVariableResolver()); 
```
### Usage 5 (Valid Properties)
``` csharp
// Only accept Id in predicate, if other properties occurs in predicate, an exception will be thrown
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("id = 1 and name='Test'", validProperties: new[] { "Id" }); // throw an exception
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
var result = context.Entities.Where(condition).ToList();
// or paging
var result = context.Take(condition, new Pagination { Offset = 10, Limit = 50 });
// or paging with total records count
var result = context.TakePage(condition, new Pagination { Offset = 10, Limit = 50 });
```
### Support opetors
| Branch             | Usage|Support data types|
|--------------------|--------------------------------------------|-------------------------------------------------------------------|
|Equals| Id = 1 or Id == 1 | Number, String, Guid, Boolean, DateTime, DateTimeOffset, Enum, Nullable |
|Not Equals| Id != 1 or Id <> 1 | Number, String, Guid, Boolean, DateTime, DateTimeOffset, Enum, Nullable |
|Less than| Id < 1 | Number, DateTime, DateTimeOffset, Enum, Nullable |
|Less than or Equal| Id <= 1 | Number, DateTime, DateTimeOffset, Enum, Nullable |
|Greater than| Id > 1 | Number, DateTime, DateTimeOffset, Enum, Nullable |
|Greater than or Equal| Id >= 1 | Number, DateTime, DateTimeOffset, Enum, Nullable |
|In| Id in [1, 2, 3, 4] or Name in ['String1', 'String2'] | Number, String, Guid, DateTime, DateTimeOffset, Enum, Nullable |
|Contains | Name.contains('Text') or Name @ 'Text' | String |
|Starts with | Name.startsWith('Text') or Name @* 'Text' | String |
|Ends with | Name.endsWith('Text') or Name \*@ 'Text' | String |
|Not | !IsEnabled or not(IsEnabled) or not(Id = 1) or !(Id = 1) | Boolean |
|Logical and (and or &&) | Id = 1 and Name = "Text" |----|
|Logical or (or or \|\|) | Id = 1 and Name = "Text" |----|
