using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kkts.Expressions.OpenSearch.Tests.Integration
{
    public enum ItemStatus { Inactive = 0, Active = 1, Suspended = 2 }

    public sealed class Item
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Email { get; set; }
        public string Body { get; set; }
        public ItemStatus Status { get; set; }
        public int Count { get; set; }
        public long Big { get; set; }
        public int? Rating { get; set; }
        public double? Weight { get; set; }
        public decimal Price { get; set; }
        public DateTime Created { get; set; }
        public DateTime? Closed { get; set; }
        public Customer Customer { get; set; }
        public Meta Meta { get; set; }
        public List<Line> Lines { get; set; }
        public List<Part> Parts { get; set; }
        public List<string> Tags { get; set; }
        public List<int> Numbers { get; set; }
        public List<int?> Values { get; set; }

        /// <summary>Indexes a non-null <see cref="Meta"/> as <c>{}</c> instead of an object with explicit null children.</summary>
        [JsonIgnore]
        public bool IndexMetaAsEmptyObject { get; set; }
    }

    public sealed class Customer
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    public sealed class Meta
    {
        public string Label { get; set; }
        public int? Score { get; set; }
    }

    public sealed class Line
    {
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal? Discount { get; set; }
        public Product Product { get; set; }
        public List<Tax> Taxes { get; set; }
    }

    public sealed class Product
    {
        public string Name { get; set; }
    }

    public sealed class Tax
    {
        public string Code { get; set; }
        public double Rate { get; set; }
    }

    public sealed class Part
    {
        public string Kind { get; set; }
        public int Size { get; set; }
    }
}
