﻿using System.Linq.Expressions;

namespace Kkts.Expressions.Internal.Nodes
{
	internal class Group : Node
	{
		public Node Node { get; set; }

		public override Expression Build(BuildArgument arg)
		{
			return Node.Build(arg);
		}
	}
}
