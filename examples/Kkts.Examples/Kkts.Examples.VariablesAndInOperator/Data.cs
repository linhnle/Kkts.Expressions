using Microsoft.EntityFrameworkCore;

namespace Kkts.Examples.VarablesAndInOperator;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
    : base(options)
    { }
    public DbSet<User> Users { get; set; }

    public DbSet<Project> Projects { get; set; }

    public static readonly DbContextOptions<TestDbContext> Options = new DbContextOptionsBuilder<TestDbContext>()
                                                   .UseInMemoryDatabase(databaseName: "Test")
                                                   .Options;

    static TestDbContext()
    {
        var dbContext = new TestDbContext(Options);
        dbContext.Projects.Add(new Project { Id = 1, Name = "project 1" });
        dbContext.SaveChanges();
        foreach (var id in Enumerable.Range(1, 20))
        {
            dbContext.Users.Add(new User { Id = id, Username = $"Username {id}", Email = $"email{id}@mail.com", ProjectId = 1 });
        }

        dbContext.SaveChanges();
    }
}

public record class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    public Project? Project { get; set; }
}

public record class Project
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<User>? Users { get; set; }
}