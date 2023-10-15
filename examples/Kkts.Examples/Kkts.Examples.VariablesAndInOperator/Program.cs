using Kkts.Examples.VarablesAndInOperator;
using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;

var context = new TestDbContext(TestDbContext.Options);
var resolver = new CustomVariableResolver(context);

// init variables: user.id and project.*
await resolver.InitializeVariablesAsync(null!);

var predicate = await Interpreter.ParsePredicateAsync<User>("id in [$user.id]", variableResolver: resolver);
var predicate2 = await Interpreter.ParsePredicateAsync<User>("id in $userIds", variableResolver: resolver);
var predicate3 = await Interpreter.ParsePredicateAsync<User>("id in [1, 2, 3]", variableResolver: resolver);

var users = await context.Users.Where(predicate.Result).ToListAsync();
var users2 = await context.Users.Where(predicate2.Result).ToListAsync();
var users3 = await context.Users.Where(predicate3.Result).ToListAsync();

Console.WriteLine("---------------------------------------");
foreach (var user in users)
{
    Console.WriteLine(user);
}

Console.WriteLine("---------------------------------------");
foreach (var user in users2)
{
    Console.WriteLine(user);
}

Console.WriteLine("---------------------------------------");
foreach (var user in users3)
{
    Console.WriteLine(user);
}


Console.ReadKey();