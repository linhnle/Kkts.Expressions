using System;

namespace Kkts.Expressions.UnitTest
{
    internal class CustomVariableResolver : VariableResolver
    {
        public UserInfo User { get; } = new UserInfo { Id = DF.Integer1, UserName = "linhle" };

        public int[] UserIds { get; } = { DF.Integer1 };
    }

    internal class UserInfo
    {
        public string UserName { get; set; }

        public int Id { get; set; }
    }
}
