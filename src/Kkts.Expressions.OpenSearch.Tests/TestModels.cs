using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kkts.Expressions.OpenSearch.Tests
{
    public enum Status
    {
        Inactive = 0,
        Active = 1,
        Suspended = 2
    }

    public class Data
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Code { get; set; }

        public string Description { get; set; }

        public int? NullableInteger { get; set; }

        public bool IsEnabled { get; set; }

        public bool? NullableFlag { get; set; }

        public decimal Price { get; set; }

        public double Score { get; set; }

        public float Ratio { get; set; }

        public int IntValue { get; set; }

        public long LongValue { get; set; }

        public long? NullableLong { get; set; }

        public ulong ULongValue { get; set; }

        public uint UIntValue { get; set; }

        public short ShortValue { get; set; }

        public byte ByteValue { get; set; }

        public sbyte SByteValue { get; set; }

        public char Grade { get; set; }

        public DateTime Created { get; set; }

        public DateTime? Deleted { get; set; }

        public DateTimeOffset Updated { get; set; }

        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public Guid Key { get; set; }

        public Status Status { get; set; }

        public Status? NullableStatus { get; set; }

        public TimeSpan Duration { get; set; }

        [JsonPropertyName("full_name")]
        public string FullName { get; set; }

        public Meta Meta { get; set; }

        public Customer Customer { get; set; }

        public List<OrderLine> Lines { get; set; }

        public List<string> Tags { get; set; }

        public List<int?> Values { get; set; }

        public int[] Numbers { get; set; }

        public Data Parent { get; set; }
    }

    public class Meta
    {
        public DateTime Created { get; set; }

        public string Owner { get; set; }
    }

    public class Customer
    {
        public string Name { get; set; }

        public int Age { get; set; }

        public Address Address { get; set; }

        public List<Contact> Contacts { get; set; }
    }

    public class Address
    {
        public string City { get; set; }

        public string Street { get; set; }
    }

    public class Contact
    {
        public string Email { get; set; }
    }

    public class OrderLine
    {
        public string Sku { get; set; }

        public int Quantity { get; set; }

        public int? Discount { get; set; }

        public List<Tax> Taxes { get; set; }

        public List<string> Labels { get; set; }

        public Product Product { get; set; }
    }

    public class Tax
    {
        public string Code { get; set; }

        public decimal Rate { get; set; }
    }

    public class Product
    {
        public string Name { get; set; }
    }
}
