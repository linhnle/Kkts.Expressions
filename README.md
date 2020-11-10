# Kkts.Expressions
Build string expression to lambda expression
get via nuget **[Kkts.Expressions](https://www.nuget.org/packages/Kkts.Expressions)** 
#### usage
``` csharp
class Data
{
  public int Id { get; set; }
  public string Name { get; set; }
  public bool IsEnabled { get; set; }
}

// The property name is case insensitive for example id and Id are the same
Expression<Func<Data, bool>> predicate = Interpreter.ParsePredicate("id = 1 and name='Test'"); 

// or
var filters = new Filter[]
            {
                new Filter{ Property = "id", Operator = "=", Value = "1" },
                new Filter{ Property = "name", Operator = "=", Value = "Test" }
            };

Expression<Func<Data, bool>> predicate = filters.BuildPredicate<Data>();
```
### Support opeartors
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
