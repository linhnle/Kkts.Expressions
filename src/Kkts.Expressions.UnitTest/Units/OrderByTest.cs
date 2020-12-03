using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Kkts.Expressions.UnitTest.Units
{
    public class OrderByTest
    {
        [Fact]
        public void OrderBy_Expression_Success()
        {
            using (var context = DF.GetContext())
            {
                var value = context.Entities.OrderBy("OrderBy1").FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy("OrderBy1 asc").FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy("OrderBy1 desc").FirstOrDefault();
                Assert.Equal(50, value.OrderBy1);
                value = context.Entities.Where(p => p.OrderBy1 < 50).OrderBy("OrderBy1 desc, OrderBy2 desc").FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);
            }
        }

        [Fact]
        public void OrderBy_OrderByInfo_Success()
        {
            using (var context = DF.GetContext())
            {
                var value = context.Entities.OrderBy(new[] { new OrderByInfo { Property = "OrderBy1" } }).FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy(new[] { new OrderByInfo { Property = "OrderBy1", Descending = true } }).FirstOrDefault();
                Assert.Equal(50, value.OrderBy1);
                value = context.Entities.Where(p => p.OrderBy1 < 50).OrderBy(new[] { new OrderByInfo { Property = "OrderBy1", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }).FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);
            }
        }

        [Fact]
        public void TryOrderBy_Success()
        {
            using (var context = DF.GetContext())
            {
                var result = context.Entities.Where(p => p.OrderBy1 < 50).TryOrderBy("OrderBy1 desc, OrderBy2 desc");
                Assert.True(result.Succeeded);
                var value = result.Result.FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);

                result = context.Entities.Where(p => p.OrderBy1 < 50).TryOrderBy(new[] { new OrderByInfo { Property = "OrderBy1", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } });
                Assert.True(result.Succeeded);
                value = result.Result.FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);

                result = context.Entities.TryOrderBy("OrderBy1 desc, OrderBy2 desc", new[] { "OrderBy1" });
                Assert.False(result.Succeeded);
                Assert.True(result.InvalidProperties.Count() == 1 && result.InvalidProperties.First() == "OrderBy2");

                result = context.Entities.TryOrderBy(new[] { new OrderByInfo { Property = "OrderBy1", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, new[] { "OrderBy1" });
                Assert.False(result.Succeeded);
                Assert.True(result.InvalidProperties.Count() == 1 && result.InvalidProperties.First() == "OrderBy2");
            }
        }



        [Fact]
        public void OrderBy_Expression_PropertyMapping_Success()
        {
            var propertyMapping = new Dictionary<string, string> { ["Name"] = "OrderBy1" };
            using (var context = DF.GetContext())
            {
                var value = context.Entities.OrderBy("Name", propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy("Name asc", propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy("Name desc", propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal(50, value.OrderBy1);
                value = context.Entities.Where(p => p.OrderBy1 < 50).OrderBy("Name desc, OrderBy2 desc", propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);
                value = context.Entities.OrderBy(new[] { new OrderByInfo { Property = "Name" } }, propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal(10, value.OrderBy1);
                value = context.Entities.OrderBy(new[] { new OrderByInfo { Property = "Name", Descending = true } }, propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal(50, value.OrderBy1);
                value = context.Entities.Where(p => p.OrderBy1 < 50).OrderBy(new[] { new OrderByInfo { Property = "Name", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, propertyMapping: propertyMapping).FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);

                var result = context.Entities.Where(p => p.OrderBy1 < 50).TryOrderBy("Name desc, OrderBy2 desc", propertyMapping: propertyMapping);
                Assert.True(result.Succeeded);
                value = result.Result.FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);

                result = context.Entities.Where(p => p.OrderBy1 < 50).TryOrderBy(new[] { new OrderByInfo { Property = "Name", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, propertyMapping: propertyMapping);
                Assert.True(result.Succeeded);
                value = result.Result.FirstOrDefault();
                Assert.Equal("EBC", value.OrderBy2);

                result = context.Entities.TryOrderBy("Name desc, OrderBy2 desc", new[] { "Name" }, propertyMapping: propertyMapping);
                Assert.False(result.Succeeded);
                Assert.True(result.InvalidProperties.Count() == 1 && result.InvalidProperties.First() == "OrderBy2");

                result = context.Entities.TryOrderBy(new[] { new OrderByInfo { Property = "Name", Descending = true }, new OrderByInfo { Property = "OrderBy2", Descending = true } }, new[] { "Name" }, propertyMapping: propertyMapping);
                Assert.False(result.Succeeded);
                Assert.True(result.InvalidProperties.Count() == 1 && result.InvalidProperties.First() == "OrderBy2");
            }
        }
    }
}
