using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kkts.Expressions.UnitTest
{
    public class TestEntity
    {
        public int Id { get; set; }
        public bool Boolean { get; set; }
        public bool? BooleanNullable { get; set; }
        public int Integer { get; set; }
        public int? IntegerNullable { get; set; }
        public double Double { get; set; }
        public double? DoubleNullable { get; set; }
        public string String { get; set; }
        public Guid Guid { get; set; }
        public Guid? GuidNullable { get; set; }
        public TestOptions Option { get; set; }
        public TestOptions? OptionNullable { get; set; }
        public DateTime DateTime { get; set; }
        public DateTime? DateTimeNullable { get; set; }
        public DateTimeOffset DateTimeOffset { get; set; }
        public DateTimeOffset? DateTimeOffsetNullable { get; set; }

        [InverseProperty(nameof(Parent))]
        public int ParentId { get; set; }
        public ParentEntity Parent { get; set; }
    }

    public class ParentEntity
    {
        public int Id { get; set; }

        public string Name { get; set; }
    }

    public enum TestOptions
    {
        Option1,
        Option2,
        Option3,
        OptionN
    }
}
