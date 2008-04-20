﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using System.Linq.Expressions;

namespace Moq.Tests
{
	public class StubFixture
	{

		[Fact]
		public void ShouldExpectPropertyGetterAndSetterWithSameExpression()
		{
			var mock = new Mock<IFoo>();

			mock.ExpectGet(x => x.ValueProperty).Returns(25);
			mock.ExpectSet(x => x.ValueProperty);

			mock.Object.ValueProperty = 7;
			Assert.Equal(25, mock.Object.ValueProperty);
		}

		[Fact]
		public void ShouldExpectPropertyGetterAndSetterWithoutSameExpression()
		{
			var mock = new Mock<IFoo>();

			mock.ExpectGet(x => x.ValueProperty).Returns(25);
			mock.ExpectSet(y => y.ValueProperty);

			mock.Object.ValueProperty = 7;
			Assert.Equal(25, mock.Object.ValueProperty);
		}
	}

	public interface IFoo
	{
		int ValueProperty { get; set; }
	}

	public static class PropertyStub
	{
		public static void Stub<T, TProperty>(this Mock<T> mock, Expression<Func<T, TProperty>> property)
			 where T : class
		{
			mock.Stub(property, default(TProperty));
		}

		public static void Stub<T, TProperty>(this Mock<T> mock, Expression<Func<T, TProperty>> property, TProperty defaultValue)
			 where T : class
		{
			TProperty value = defaultValue;
			mock.ExpectGet(property).Returns(() => value);
			mock.ExpectSet(property).Callback(p => value = p);
		}
	}

}
