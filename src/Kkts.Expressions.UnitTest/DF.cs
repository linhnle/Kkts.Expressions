using Microsoft.EntityFrameworkCore;
using System;

namespace Kkts.Expressions.UnitTest
{
    public class DF
    {
        public static readonly DbContextOptions<TestDbContext> Options = new DbContextOptionsBuilder<TestDbContext>()
                                                       .UseInMemoryDatabase(databaseName: "Test")
                                                       .Options;
        public static readonly int ParentId1 = 1;
        public static readonly int ParentId2 = 2;
        public static readonly int ParentId3 = 3;
        public static readonly int ParentIdN = 4;
        public static readonly int Integer1 = 2;
        public static readonly int Integer2 = 4;
        public static readonly int Integer3 = 8;
        public static readonly int IntegerN = 16;
        public static readonly DateTime DateTime1 = new DateTime(2016, 05, 15);
        public static readonly DateTime DateTime2 = new DateTime(2017, 06, 15);
        public static readonly DateTime DateTime3 = new DateTime(2019, 07, 15);
        public static readonly DateTime DateTimeN = new DateTime(2020, 08, 15);
        public static readonly string DateTimeString1 = DateTime1.ToString("yyyy-MM-dd");
        public static readonly string DateTimeString2 = DateTime2.ToString("yyyy-MM-dd");
        public static readonly string DateTimeString3 = DateTime3.ToString("yyyy-MM-dd");
        public static readonly string DateTimeStringN = DateTimeN.ToString("yyyy-MM-dd");
        public static readonly DateTimeOffset DateTimeOffset1 = new DateTimeOffset(2016, 05, 16, 0, 0, 0, TimeSpan.FromSeconds(0));
        public static readonly DateTimeOffset DateTimeOffset2 = new DateTimeOffset(2017, 06, 16, 0, 0, 0, TimeSpan.FromSeconds(0));
        public static readonly DateTimeOffset DateTimeOffset3 = new DateTimeOffset(2019, 07, 16, 0, 0, 0, TimeSpan.FromSeconds(0));
        public static readonly DateTimeOffset DateTimeOffsetN = new DateTimeOffset(2020, 08, 16, 0, 0, 0, TimeSpan.FromSeconds(0));
        public static readonly string DateTimeOffsetString1 = DateTimeOffset1.ToString();
        public static readonly string DateTimeOffsetString2 = DateTimeOffset2.ToString();
        public static readonly string DateTimeOffsetString3 = DateTimeOffset3.ToString();
        public static readonly string DateTimeOffsetStringN = DateTimeOffsetN.ToString();
        public static readonly double Double1 = 2d;
        public static readonly double Double2 = 4d;
        public static readonly double Double3 = 8.3d;
        public static readonly double DoubleN = 16.12d;
        public static readonly string DoubleString1 = "2.0";
        public static readonly string DoubleString2 = "4.0";
        public static readonly string DoubleString3 = "8.3";
        public static readonly string DoubleStringN = "16.12";
        public static readonly Guid Guid1 = Guid.NewGuid();
        public static readonly Guid Guid2 = Guid.NewGuid();
        public static readonly Guid Guid3 = Guid.NewGuid();
        public static readonly Guid GuidN = Guid.NewGuid();
        public static readonly string String1 = "Name 1";
        public static readonly string String2 = "Name 2";
        public static readonly string String3 = "Name 3";
        public static readonly string StringN = "Name 4";

        static DF()
        {
            Seed();
        }

        public static TestDbContext GetContext()
        {
            return new TestDbContext(Options);
        }

        private static void Seed()
        {
            using(var context = GetContext())
            {
                for(var i = 1; i <= ParentIdN; ++i)
                {
                    var p = new ParentEntity
                    {
                        Id = i,
                        Name = "Parent " + i
                    };

                    context.Add(p);
                }

                //var random = new Random();
                //for(var i = 1; i <= 999; ++i)
                //{
                //    var e = new TestEntity
                //    {
                //        DateTime = new DateTime(2015 + i % 6, i % 12 + 1, i % 28 + 1),
                //        DateTimeNullable = i % 3 < 2 ? (DateTime?)null : new DateTime(2015 + i % 6, i % 12 + 1, i % 12 + 12),
                //        DateTimeOffset = new DateTime(2015 + i % 6, i % 8 + 1, i % 14 + 1),
                //        DateTimeOffsetNullable = i % 5 < 4 ? (DateTimeOffset?)null : new DateTimeOffset(2019, i % 12 + 1, i % 12 + 12, i % 24, i % 60, 0, new TimeSpan(0, 0, 0)),
                //        Double = i % 10 == 0 ? i : random.NextDouble(),
                //        DoubleNullable = i % 10 < 9 ? (double?)null : i % 10 < 5 ? i : random.NextDouble(),
                //        Guid = Guid.NewGuid(),
                //        GuidNullable = i % 10 == 0 ? (Guid?)null : Guid.NewGuid(),
                //        Integer = i % 200,
                //        IntegerNullable = i % 10 < 5 ? i % 100 : (int?)null,
                //        Option = (TestOptions)(i % 2),
                //        OptionNullable = i % 10 < 5 ? (TestOptions)(i % 2) : (TestOptions?)null,
                //        ParentId = i % 4 + 1,
                //        String = "Name " + i.ToString("0000")
                //    };

                //    context.Add(e);
                //}

                var e1 = new TestEntity
                {
                    DateTime = DateTime1,
                    DateTimeNullable = DateTime1,
                    DateTimeOffset = DateTimeOffset1,
                    DateTimeOffsetNullable = DateTimeOffset1,
                    Double = Double1,
                    DoubleNullable = Double1,
                    Guid = Guid1,
                    GuidNullable = Guid1,
                    Integer = Integer1,
                    IntegerNullable = Integer1,
                    Option = TestOptions.Option1,
                    OptionNullable = TestOptions.Option1,
                    ParentId = ParentId1,
                    String = String1
                };

                var e2 = new TestEntity
                {
                    DateTime = DateTime2,
                    DateTimeNullable = DateTime2,
                    DateTimeOffset = DateTimeOffset2,
                    DateTimeOffsetNullable = DateTimeOffset2,
                    Double = Double2,
                    DoubleNullable = Double2,
                    Guid = Guid2,
                    GuidNullable = Guid2,
                    Integer = Integer2,
                    IntegerNullable = Integer2,
                    Option = TestOptions.Option2,
                    OptionNullable = TestOptions.Option2,
                    ParentId = ParentId2,
                    String = String2
                };

                var e3 = new TestEntity
                {
                    DateTime = DateTime3,
                    DateTimeNullable = DateTime3,
                    DateTimeOffset = DateTimeOffset3,
                    DateTimeOffsetNullable = DateTimeOffset3,
                    Double = Double3,
                    DoubleNullable = Double3,
                    Guid = Guid3,
                    GuidNullable = Guid3,
                    Integer = Integer3,
                    IntegerNullable = Integer3,
                    Option = TestOptions.Option3,
                    OptionNullable = TestOptions.Option3,
                    ParentId = ParentId3,
                    String = String3
                };

                var eN = new TestEntity
                {
                    DateTime = DateTimeN,
                    DateTimeNullable = DateTimeN,
                    DateTimeOffset = DateTimeOffsetN,
                    DateTimeOffsetNullable = DateTimeOffsetN,
                    Double = DoubleN,
                    DoubleNullable = DoubleN,
                    Guid = GuidN,
                    GuidNullable = GuidN,
                    Integer = IntegerN,
                    IntegerNullable = IntegerN,
                    Option = TestOptions.OptionN,
                    OptionNullable = TestOptions.OptionN,
                    ParentId = ParentIdN,
                    String = StringN,
                    BooleanNullable = true
                };

                var e5 = new TestEntity
                {
                    DateTime = DateTime2,
                    DateTimeNullable = null,
                    DateTimeOffset = DateTimeOffset2,
                    DateTimeOffsetNullable = null,
                    Double = Double2,
                    DoubleNullable = null,
                    Guid = Guid2,
                    GuidNullable = null,
                    Integer = Integer2,
                    IntegerNullable = null,
                    Option = TestOptions.Option2,
                    OptionNullable = null,
                    ParentId = ParentId2,
                    String = null,
                    Boolean = true,
                    BooleanNullable = null
                };

                context.Add(e1);
                context.Add(e2);
                context.Add(e3);
                context.Add(eN);
                context.Add(e5);
                context.SaveChanges();
            }
        }
    }
}
