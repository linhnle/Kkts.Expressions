using Microsoft.EntityFrameworkCore;

namespace Kkts.Expressions.UnitTest
{
    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
        { }
        public DbSet<TestEntity> Entities { get; set; }

        public DbSet<ParentEntity> Parents { get; set; }
    }
}
