using System;
using System.Collections.Generic;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    internal static class SeedData
    {
        private static DateTime Utc(int day, int millisecond = 0) => new DateTime(2024, 1, day, 0, 0, 0, millisecond, DateTimeKind.Utc);

        // Sort keys used by the equivalence suite (Code, numbers, dates) order identically in CLR and OpenSearch.
        public static IReadOnlyList<Item> Create() => new[]
        {
            new Item
            {
                Id = 1, Code = "a1", Name = "alpha", Title = "First Title", Email = "Ann@Example.com", Body = "quick brown fox",
                Status = ItemStatus.Active, Count = 1, Big = 9007199254740991, Rating = 5, Weight = 1.5, Price = 10.5m,
                Created = Utc(1, 123), Customer = new Customer { Name = "ann", Age = 30 },
                Meta = new Meta { Label = "x", Score = 1 },
                Lines = new List<Line>
                {
                    new Line { Sku = "A", Quantity = 3, Product = new Product { Name = "pa" }, Taxes = new List<Tax> { new Tax { Code = "VAT", Rate = 0.2 } } },
                    new Line { Sku = "B", Quantity = 1, Discount = 0.5m, Taxes = new List<Tax>() }
                },
                Parts = new List<Part> { new Part { Kind = "bolt", Size = 1 }, new Part { Kind = "nut", Size = 5 } },
                Tags = new List<string> { "red", "blue" }, Numbers = new List<int> { 1, 2 }, Values = new List<int?> { 1, null }
            },
            new Item
            {
                Id = 2, Code = "b2", Name = "Beta", Title = "second title", Email = "bob@example.com",
                Status = ItemStatus.Inactive, Count = 2, Big = 9007199254740992, Price = 20m,
                Created = Utc(2, 250), Closed = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Lines = new List<Line>
                {
                    new Line { Sku = "A", Quantity = 1, Discount = 0.1m, Product = new Product { Name = "pb" }, Taxes = new List<Tax> { new Tax { Code = "GST", Rate = 0.05 } } }
                },
                Parts = new List<Part> { new Part { Kind = "bolt", Size = 5 } },
                Tags = new List<string> { "green" }, Numbers = new List<int>(), Values = new List<int?> { 2 }
            },
            new Item
            {
                Id = 3, Code = "c3", Name = "special *?\\ marker", Title = "Third", Email = "CAROL@example.com", Body = "slow",
                Status = ItemStatus.Suspended, Count = 3, Big = 9007199254740993, Rating = 3, Weight = 2.25, Price = 30.25m,
                Created = Utc(3, 999), Customer = new Customer { Name = "carol", Age = 45 },
                Meta = new Meta(), IndexMetaAsEmptyObject = true,
                Lines = new List<Line>(), Parts = new List<Part>(), Tags = new List<string>(), Numbers = new List<int> { 3 }, Values = new List<int?>()
            },
            new Item
            {
                Id = 4, Code = "d4", Status = ItemStatus.Active, Count = int.MaxValue, Big = long.MaxValue, Rating = 1, Weight = 0.1, Price = 0m,
                Created = Utc(4, 1), Customer = new Customer { Age = 18 }, Meta = new Meta()
            },
            new Item
            {
                Id = 5, Code = "e5", Name = "alphabet", Title = "Fifth title", Email = "dave@EXAMPLE.com",
                Status = ItemStatus.Inactive, Count = -5, Big = -9007199254740993, Rating = 4, Weight = -1, Price = 5.75m,
                Created = Utc(5, 500), Closed = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Customer = new Customer { Name = "ann", Age = 60 }, Meta = new Meta { Label = "y" },
                Lines = new List<Line>
                {
                    new Line
                    {
                        Sku = "C", Quantity = 10, Product = new Product(),
                        Taxes = new List<Tax> { new Tax { Code = "VAT", Rate = 0.1 }, new Tax { Code = "GST", Rate = 0.2 } }
                    }
                },
                Parts = new List<Part> { new Part { Kind = "nut", Size = 1 } },
                Tags = new List<string> { "red" }, Numbers = new List<int> { 5, 6 }, Values = new List<int?> { null }
            },
            new Item
            {
                Id = 6, Code = "f6", Name = string.Empty, Title = string.Empty, Email = string.Empty,
                Status = ItemStatus.Active, Count = 0, Big = 0, Price = 1m, Created = Utc(6),
                Customer = new Customer { Name = string.Empty, Age = 0 },
                Lines = new List<Line> { new Line { Sku = "A", Quantity = 5, Taxes = new List<Tax>() } },
                Tags = new List<string> { "blue" }, Numbers = new List<int> { 2 }
            },
            new Item
            {
                Id = 7, Code = "g7", Name = "gamma", Title = "Seventh", Email = "eve@example.com",
                Status = ItemStatus.Suspended, Count = -2147483648, Big = long.MinValue, Rating = 2, Weight = 3.5, Price = 99.99m,
                Created = Utc(7, 7), Meta = new Meta { Score = 7 },
                Lines = new List<Line> { new Line { Sku = "D", Quantity = 2, Discount = 0.25m, Product = new Product { Name = "pd" }, Taxes = new List<Tax> { new Tax { Code = "VAT", Rate = 0.05 } } } },
                Parts = new List<Part> { new Part { Kind = "washer", Size = 2 } },
                Tags = new List<string> { "red", "green" }, Numbers = new List<int> { 7 }, Values = new List<int?> { 7 }
            }
        };
    }
}
