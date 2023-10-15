using Kkts.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Kkts.Examples.VariableResolver
{
    public class CustomVariableResolver : Expressions.VariableResolver
    {
        private readonly TestDbContext _context;
        public CustomVariableResolver(TestDbContext context) 
        {
            _context = context;
        }

        public Project? Project { get; set; }

// Method 1:
        protected override async Task<VariableInfo> TryResolveCore(string name, CancellationToken cancellationToken)
        => name.ToLower() switch
        {
            "userids" => new VariableInfo { Name = name, Resolved = true, Value = await _context.Users.Select(p => p.Id).Take(10).ToListAsync() },
            _ => await base.TryResolveCore(name, cancellationToken),
        };
// ---------------

        public override Task InitializeVariablesAsync(object state)
        {
// Method 2:
            // case insentitive
            TryAdd("User.Id", _context.Users.First().Id);
// --------------- 

// Method 3:
            // init variable project.id and project.name
            Project = _context.Projects.First();
// ---------------            
            return base.InitializeVariablesAsync(state);
        }
    }
}
