using Kkts.Examples.VariableResolver;
using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;

var context = new TestDbContext(TestDbContext.Options);
var resolver = new CustomVariableResolver(context);

// init variables: user.id and project.*
await resolver.InitializeVariablesAsync(null!);

var predicate = await Interpreter.ParsePredicateAsync<User>("(id = $user.id and id in $userIds) and project.id = $project.id", variableResolver: resolver);

var users = await context.Users.Where(predicate.Result).ToListAsync();

foreach (var user in users)
{
    Console.WriteLine(user);
}

Console.ReadKey();