using System;
using Microsoft.EntityFrameworkCore;

namespace Kkts.Expressions.EntityFrameworkCore.Tests;

public enum RelationalStatus
{
    Active,
    Inactive,
    Archived
}

public sealed class RelationalRecord
{
    public int Id { get; set; }
    public int Integer { get; set; }
    public int? NullableInteger { get; set; }
    public sbyte SByteValue { get; set; }
    public byte ByteValue { get; set; }
    public short ShortValue { get; set; }
    public ushort UShortValue { get; set; }
    public uint UIntValue { get; set; }
    public long LongValue { get; set; }
    public ulong ULongValue { get; set; }
    public float FloatValue { get; set; }
    public double DoubleValue { get; set; }
    public decimal DecimalValue { get; set; }
    public decimal? NullableDecimalValue { get; set; }
    public bool Enabled { get; set; }
    public bool? NullableEnabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public RelationalStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? OptionalCreatedAt { get; set; }
    public int ParentId { get; set; }
    public RelationalParent Parent { get; set; } = null!;
}

public sealed class RelationalParent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public abstract class RelationalTestDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<RelationalRecord> Records => Set<RelationalRecord>();
    public DbSet<RelationalParent> Parents => Set<RelationalParent>();

    protected abstract string DefaultCollation { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation(DefaultCollation);
        modelBuilder.Entity<RelationalParent>(entity =>
        {
            entity.HasKey(parent => parent.Id);
            entity.Property(parent => parent.Id).ValueGeneratedNever();
            entity.Property(parent => parent.Name)
                .IsRequired()
                .UseCollation(DefaultCollation);
        });

        modelBuilder.Entity<RelationalRecord>(entity =>
        {
            entity.HasKey(record => record.Id);
            entity.Property(record => record.Id).ValueGeneratedNever();
            entity.Property(record => record.Name)
                .IsRequired()
                .UseCollation(DefaultCollation);
            entity.Property(record => record.CreatedAt).HasPrecision(3);
            entity.Property(record => record.OptionalCreatedAt).HasPrecision(3);
            entity.Property(record => record.Status).HasConversion<string>();
            entity.HasOne(record => record.Parent)
                .WithMany()
                .HasForeignKey(record => record.ParentId);
        });
    }
}

public sealed class SqlServerRelationalTestDbContext(DbContextOptions options)
    : RelationalTestDbContext(options)
{
    protected override string DefaultCollation => "Latin1_General_100_BIN2";
}

public sealed class MySqlRelationalTestDbContext(DbContextOptions options)
    : RelationalTestDbContext(options)
{
    protected override string DefaultCollation => "utf8mb4_0900_bin";
}
