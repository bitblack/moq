﻿//Copyright (c) 2007, Moq Team 
//http://code.google.com/p/moq/
//All rights reserved.

//Redistribution and use in source and binary forms, 
//with or without modification, are permitted provided 
//that the following conditions are met:

//    * Redistributions of source code must retain the 
//    above copyright notice, this list of conditions and 
//    the following disclaimer.

//    * Redistributions in binary form must reproduce 
//    the above copyright notice, this list of conditions 
//    and the following disclaimer in the documentation 
//    and/or other materials provided with the distribution.

//    * Neither the name of the Moq Team nor the 
//    names of its contributors may be used to endorse 
//    or promote products derived from this software 
//    without specific prior written permission.

//THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
//CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, 
//INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
//MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
//DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR 
//CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
//SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
//BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
//SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
//INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
//WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
//NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
//OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF 
//SUCH DAMAGE.

//[This is the BSD license, see
// http://www.opensource.org/licenses/bsd-license.php]

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Castle.Core.Interceptor;
using Castle.DynamicProxy;
using Moq.Language.Flow;
using System.Collections.Generic;

namespace Moq
{
	/// <typeparam name="T">Type to mock, which can be an interface or a class.</typeparam>
	/// <summary>
	/// Provides a mock implementation of <typeparamref name="T"/>.
	/// </summary>
	/// <remarks>
	/// Only abstract and virtual members of classes can be mocked.
	/// <para>
	/// The behavior of the mock with regards to the expectations and the actual calls is determined 
	/// by the optional <see cref="MockBehavior"/> that can be passed to the <see cref="Mock{T}(MockBehavior)"/> 
	/// constructor.
	/// </para>
	/// </remarks>
	/// <example group="overview" order="0">
	/// The following example shows setting expectations with specific values 
	/// for method invocations:
	/// <code>
	/// //setup - data
	/// var order = new Order(TALISKER, 50);
	/// var mock = new Mock&lt;IWarehouse&gt;();
	/// 
	/// //setup - expectations
	/// mock.Expect(x => x.HasInventory(TALISKER, 50)).Returns(true);
	/// 
	/// //exercise
	/// order.Fill(mock.Object);
	/// 
	/// //verify
	/// Assert.True(order.IsFilled);
	/// </code>
	/// The following example shows how to use the <see cref="It"/> class 
	/// to specify conditions for arguments instead of specific values:
	/// <code>
	/// //setup - data
	/// var order = new Order(TALISKER, 50);
	/// var mock = new Mock&lt;IWarehouse&gt;();
	/// 
	/// //setup - expectations
	/// //shows how to expect a value within a range
	/// mock.Expect(x => x.HasInventory(
	///			It.IsAny&lt;string&gt;(), 
	///			It.IsInRange(0, 100, Range.Inclusive)))
	///     .Returns(false);
	/// 
	/// //shows how to throw for unexpected calls. contrast with the "verify" approach of other mock libraries.
	/// mock.Expect(x => x.Remove(
	///			It.IsAny&lt;string&gt;(), 
	///			It.IsAny&lt;int&gt;()))
	///     .Throws(new InvalidOperationException());
	/// 
	/// //exercise
	/// order.Fill(mock.Object);
	/// 
	/// //verify
	/// Assert.False(order.IsFilled);
	/// </code>
	/// </example>
	public class Mock<T> : Mock
		where T : class
	{
		static readonly ProxyGenerator generator = new ProxyGenerator();
		T instance;
		object[] constructorArguments;

		#region Ctors

		/// <summary>
		/// Ctor invoked by AsTInterface exclusively.
		/// </summary>
		private Mock(bool skipInitialize)
		{
			// HACK: this is very hackish. 
			// In order to avoid having an IMock<T> I made almost all 
			// members virtual (which has the same runtime effect on perf, btw)
			// so that As<TInterface> just overrides everything, and we avoid 
			// having members that we need just internally for the legacy 
			// extensions to work, and we don't want them publicly in an IMock 
			// interface. It's a messy issue... discuss with team.
		}

		/// <summary>
		/// Initializes an instance of the mock with <see cref="MockBehavior.Default">default behavior</see> and with 
		/// the given constructor arguments for the class. (Only valid when <typeparamref name="T"/> is a class)
		/// </summary>
		/// <remarks>
		/// The mock will try to find the best match constructor given the constructor arguments, and invoke that 
		/// to initialize the instance. This applies only for classes, not interfaces.
		/// </remarks>
		/// <example>
		/// <code>var mock = new Mock&lt;MyProvider&gt;(someArgument, 25);</code>
		/// </example>
		/// <param name="args">Optional constructor arguments if the mocked type is a class.</param>
		public Mock(params object[] args) : this(MockBehavior.Default, args) { }

		/// <summary>
		/// Initializes an instance of the mock with <see cref="MockBehavior.Default">default behavior</see>.
		/// </summary>
		/// <example>
		/// <code>var mock = new Mock&lt;IFormatProvider&gt;();</code>
		/// </example>
		public Mock() : this(MockBehavior.Default) { }

		/// <summary>
		/// Initializes an instance of the mock with the specified <see cref="MockBehavior">behavior</see>.
		/// </summary>
		/// <example>
		/// <code>var mock = new Mock&lt;IFormatProvider&gt;(MockBehavior.Relaxed);</code>
		/// </example>
		/// <param name="behavior">Behavior of the mock.</param>
		public Mock(MockBehavior behavior) : this(behavior, new object[0]) { }

		/// <summary>
		/// Initializes an instance of the mock with a specific <see cref="MockBehavior">behavior</see> with 
		/// the given constructor arguments for the class.
		/// </summary>
		/// <remarks>
		/// The mock will try to find the best match constructor given the constructor arguments, and invoke that 
		/// to initialize the instance. This applies only to classes, not interfaces.
		/// </remarks>
		/// <example>
		/// <code>var mock = new Mock&lt;MyProvider&gt;(someArgument, 25);</code>
		/// </example>
		/// <param name="behavior">Behavior of the mock.</param>
		/// <param name="args">Optional constructor arguments if the mocked type is a class.</param>
		public Mock(MockBehavior behavior, params object[] args)
		{
			if (args == null) args = new object[0];

			this.Behavior = behavior;
			this.Interceptor = new Interceptor(behavior, typeof(T), this);
			this.constructorArguments = args;
			this.ImplementedInterfaces.Add(typeof(IMocked<T>));

			CheckParameters();
		}

		private void CheckParameters()
		{
			if (!typeof(T).IsMockeable())
				throw new ArgumentException(Properties.Resources.InvalidMockClass);

			if (typeof(T).IsInterface && this.constructorArguments.Length > 0)
				throw new ArgumentException(Properties.Resources.ConstructorArgsForInterface);
		}

		#endregion

		#region Properties

		/// <summary>
		/// Exposes the mocked object instance.
		/// </summary>
		public virtual new T Object
		{
			get
			{
				if (this.instance == null)
				{
					InitializeInstance();
				}
				return instance;
			}
		}

		private void InitializeInstance()
		{
			var mockType = typeof(T);

			try
			{
				if (mockType.IsInterface)
				{
					instance
						= (T)generator.CreateInterfaceProxyWithoutTarget(mockType, base.ImplementedInterfaces.ToArray(), Interceptor);
				}
				else
				{
					try
					{
						if (constructorArguments.Length > 0)
						{
							var generatedType = generator.ProxyBuilder.CreateClassProxy(mockType, base.ImplementedInterfaces.ToArray(), new ProxyGenerationOptions());
							instance
								= (T)Activator.CreateInstance(generatedType,
									new object[] { new IInterceptor[] { Interceptor } }.Concat(constructorArguments).ToArray());
						}
						else
						{
							instance = (T)generator.CreateClassProxy(mockType, base.ImplementedInterfaces.ToArray(), Interceptor);
						}
					}
					catch (TypeLoadException tle)
					{
						throw new ArgumentException(Properties.Resources.InvalidMockClass, tle);
					}
				}

			}
			catch (MissingMethodException mme)
			{
				throw new ArgumentException(Properties.Resources.ConstructorNotFound, mme);
			}
		}

		/// <summary>
		/// Returns the mocked object value.
		/// </summary>
		protected override object GetObject()
		{
			return Object;
		}

		internal override Type MockedType { get { return typeof(T); } }

		#endregion

		#region Expect

		/// <summary>
		/// Sets an expectation on the mocked type for a call to 
		/// to a void method.
		/// </summary>
		/// <remarks>
		/// If more than one expectation is set for the same method or property, 
		/// the latest one wins and is the one that will be executed.
		/// </remarks>
		/// <param name="expression">Lambda expression that specifies the expected method invocation.</param>
		/// <example group="expectations">
		/// <code>
		/// var mock = new Mock&lt;IProcessor&gt;();
		/// mock.Expect(x =&gt; x.Execute("ping"));
		/// </code>
		/// </example>
		public virtual IExpect Expect(Expression<Action<T>> expression)
		{
			return Mock.SetUpExpect<T>(expression, this.Interceptor);
		}

		/// <summary>
		/// Sets an expectation on the mocked type for a call to 
		/// to a value returning method.
		/// </summary>
		/// <typeparam name="TResult">Type of the return value. Typically omitted as it can be inferred from the expression.</typeparam>
		/// <remarks>
		/// If more than one expectation is set for the same method or property, 
		/// the latest one wins and is the one that will be executed.
		/// </remarks>
		/// <param name="expression">Lambda expression that specifies the expected method invocation.</param>
		/// <example group="expectations">
		/// <code>
		/// mock.Expect(x =&gt; x.HasInventory("Talisker", 50)).Returns(true);
		/// </code>
		/// </example>
		public virtual IExpect<TResult> Expect<TResult>(Expression<Func<T, TResult>> expression)
		{
			return SetUpExpect(expression, this.Interceptor);
		}

		/// <summary>
		/// Sets an expectation on the mocked type for a call to 
		/// to a property getter.
		/// </summary>
		/// <remarks>
		/// If more than one expectation is set for the same property getter, 
		/// the latest one wins and is the one that will be executed.
		/// </remarks>
		/// <typeparam name="TProperty">Type of the property. Typically omitted as it can be inferred from the expression.</typeparam>
		/// <param name="expression">Lambda expression that specifies the expected property getter.</param>
		/// <example group="expectations">
		/// <code>
		/// mock.ExpectGet(x =&gt; x.Suspended)
		///     .Returns(true);
		/// </code>
		/// </example>
		public virtual IExpectGetter<TProperty> ExpectGet<TProperty>(Expression<Func<T, TProperty>> expression)
		{
			return SetUpExpectGet(expression, this.Interceptor);
		}

		/// <summary>
		/// Sets an expectation on the mocked type for a call to 
		/// to a property setter.
		/// </summary>
		/// <remarks>
		/// If more than one expectation is set for the same property setter, 
		/// the latest one wins and is the one that will be executed.
		/// </remarks>
		/// <typeparam name="TProperty">Type of the property. Typically omitted as it can be inferred from the expression.</typeparam>
		/// <param name="expression">Lambda expression that specifies the expected property setter.</param>
		/// <example group="expectations">
		/// <code>
		/// mock.ExpectSet(x =&gt; x.Suspended);
		/// </code>
		/// </example>
		public virtual IExpectSetter<TProperty> ExpectSet<TProperty>(Expression<Func<T, TProperty>> expression)
		{
			return SetUpExpectSet<T, TProperty>(expression, this.Interceptor);
		}

		/// <summary>
		/// Sets an expectation on the mocked type for a call to 
		/// to a property setter with a specific value.
		/// </summary>
		/// <remarks>
		/// More than one expectation can be set for the setter with 
		/// different values.
		/// </remarks>
		/// <typeparam name="TProperty">Type of the property. Typically omitted as it can be inferred from the expression.</typeparam>
		/// <param name="expression">Lambda expression that specifies the expected property setter.</param>
		/// <param name="value">The value expected to be set for the property.</param>
		/// <example group="expectations">
		/// <code>
		/// mock.ExpectSet(x =&gt; x.Suspended, true);
		/// </code>
		/// </example>
		public virtual IExpectSetter<TProperty> ExpectSet<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value)
		{
			return SetUpExpectSet(expression, value, this.Interceptor);
		}

		#endregion

		#region Verify

		/// <summary>
		/// Verifies that a specific invocation matching the given 
		/// expression was performed on the mock. Use in conjuntion 
		/// with the default <see cref="MockBehavior.Loose"/>.
		/// </summary>
		/// <example group="verification">
		/// This example assumes that the mock has been used, 
		/// and later we want to verify that a given invocation 
		/// with specific parameters was performed:
		/// <code>
		/// var mock = new Mock&lt;IProcessor&gt;();
		/// // exercise mock
		/// //...
		/// // Will throw if the test code didn't call Execute with a "ping" string argument.
		/// mock.Verify(proc =&gt; proc.Execute("ping"));
		/// </code>
		/// </example>
		/// <exception cref="MockException">The invocation was not performed on the mock.</exception>
		/// <param name="expression">Expression to verify.</param>
		public virtual void Verify(Expression<Action<T>> expression)
		{
			Verify(expression, Interceptor);
		}

		/// <summary>
		/// Verifies that a specific invocation matching the given 
		/// expression was performed on the mock. Use in conjuntion 
		/// with the default <see cref="MockBehavior.Loose"/>.
		/// </summary>
		/// <example group="verification">
		/// This example assumes that the mock has been used, 
		/// and later we want to verify that a given invocation 
		/// with specific parameters was performed:
		/// <code>
		/// var mock = new Mock&lt;IWarehouse&gt;();
		/// // exercise mock
		/// //...
		/// // Will throw if the test code didn't call HasInventory.
		/// mock.Verify(warehouse =&gt; warehouse.HasInventory(TALISKER, 50));
		/// </code>
		/// </example>
		/// <exception cref="MockException">The invocation was not performed on the mock.</exception>
		/// <param name="expression">Expression to verify.</param>
		/// <typeparam name="TResult">Type of return value from the expression.</typeparam>
		public virtual void Verify<TResult>(Expression<Func<T, TResult>> expression)
		{
			Verify(expression, Interceptor);
		}

		/// <summary>
		/// Verifies that a property was read on the mock. 
		/// Use in conjuntion with the default <see cref="MockBehavior.Loose"/>.
		/// </summary>
		/// <example group="verification">
		/// This example assumes that the mock has been used, 
		/// and later we want to verify that a given property 
		/// was retrieved from it:
		/// <code>
		/// var mock = new Mock&lt;IWarehouse&gt;();
		/// // exercise mock
		/// //...
		/// // Will throw if the test code didn't retrieve the IsClosed property.
		/// mock.VerifyGet(warehouse =&gt; warehouse.IsClosed);
		/// </code>
		/// </example>
		/// <exception cref="MockException">The invocation was not performed on the mock.</exception>
		/// <param name="expression">Expression to verify.</param>
		/// <typeparam name="TProperty">Type of the property to verify. Typically omitted as it can 
		/// be inferred from the expression's return type.</typeparam>
		public virtual void VerifyGet<TProperty>(Expression<Func<T, TProperty>> expression)
		{
			VerifyGet(expression, Interceptor);
		}

		/// <summary>
		/// Verifies that a property has been set on the mock. 
		/// Use in conjuntion with the default <see cref="MockBehavior.Loose"/>.
		/// </summary>
		/// <example group="verification">
		/// This example assumes that the mock has been used, 
		/// and later we want to verify that a given invocation 
		/// with specific parameters was performed:
		/// <code>
		/// var mock = new Mock&lt;IWarehouse&gt;();
		/// // exercise mock
		/// //...
		/// // Will throw if the test code didn't set the IsClosed property.
		/// mock.VerifySet(warehouse =&gt; warehouse.IsClosed);
		/// </code>
		/// </example>
		/// <exception cref="MockException">The invocation was not performed on the mock.</exception>
		/// <param name="expression">Expression to verify.</param>
		/// <typeparam name="TProperty">Type of the property to verify. Typically omitted as it can 
		/// be inferred from the expression's return type.</typeparam>
		public virtual void VerifySet<TProperty>(Expression<Func<T, TProperty>> expression)
		{
			VerifySet(expression, Interceptor);
		}

		/// <summary>
		/// Verifies that a property has been set on the mock to the given value.
		/// Use in conjuntion with the default <see cref="MockBehavior.Loose"/>.
		/// </summary>
		/// <example group="verification">
		/// This example assumes that the mock has been used, 
		/// and later we want to verify that a given invocation 
		/// with specific parameters was performed:
		/// <code>
		/// var mock = new Mock&lt;IWarehouse&gt;();
		/// // exercise mock
		/// //...
		/// // Will throw if the test code didn't set the IsClosed property to true
		/// mock.VerifySet(warehouse =&gt; warehouse.IsClosed, true);
		/// </code>
		/// </example>
		/// <exception cref="MockException">The invocation was not performed on the mock.</exception>
		/// <param name="expression">Expression to verify.</param>
		/// <param name="value">The value that should have been set on the property.</param>
		/// <typeparam name="TProperty">Type of the property to verify. Typically omitted as it can 
		/// be inferred from the expression's return type.</typeparam>
		public virtual void VerifySet<TProperty>(Expression<Func<T, TProperty>> expression, TProperty value)
		{
			VerifySet(expression, value, Interceptor);
		}

		#endregion

		#region As<TInterface>

		/// <summary>
		/// Adds an interface implementation to the mock, 
		/// allowing expectations to be set for it.
		/// </summary>
		/// <remarks>
		/// This method can only be called before the first use 
		/// of the mock <see cref="Object"/> property, at which 
		/// point the runtime type has already been generated 
		/// and no more interfaces can be added to it.
		/// <para>
		/// Also, <typeparamref name="TInterface"/> must be an 
		/// interface and not a class, which must be specified 
		/// when creating the mock instead.
		/// </para>
		/// </remarks>
		/// <exception cref="InvalidOperationException">The mock type 
		/// has already been generated by accessing the <see cref="Object"/> property.</exception>
		/// <exception cref="ArgumentException">The <typeparamref name="TInterface"/> specified 
		/// is not an interface.</exception>
		/// <example>
		/// The following example creates a mock for the main interface 
		/// and later adds <see cref="IDisposable"/> to it to verify 
		/// it's called by the consumer code:
		/// <code>
		/// var mock = new Mock&lt;IProcessor&gt;();
		/// mock.Expect(x =&gt; x.Execute("ping"));
		/// 
		/// // add IDisposable interface
		/// var disposable = mock.As&lt;IDisposable&gt;();
		/// disposable.Expect(d => d.Dispose()).Verifiable();
		/// </code>
		/// </example>
		/// <typeparam name="TInterface">Type of interface to cast the mock to.</typeparam>
		public virtual Mock<TInterface> As<TInterface>()
			where TInterface : class
		{
			if (this.instance != null && !base.ImplementedInterfaces.Contains(typeof(TInterface)))
			{
				throw new InvalidOperationException(Properties.Resources.AlreadyInitialized);
			}
			if (!typeof(TInterface).IsInterface)
			{
				throw new ArgumentException(Properties.Resources.AsMustBeInterface);
			}

			if (!base.ImplementedInterfaces.Contains(typeof(TInterface)))
			{
				base.ImplementedInterfaces.Add(typeof(TInterface));
			}

			return new AsInterface<TInterface>(this);
		}

		private class AsInterface<TInterface> : Mock<TInterface>
			where TInterface : class
		{
			Mock<T> owner;

			public AsInterface(Mock<T> owner) : base(true)
			{
				this.owner = owner;
			}

			public override IExpect<TResult> Expect<TResult>(Expression<Func<TInterface, TResult>> expression)
			{
				return Mock.SetUpExpect(expression, owner.Interceptor);
			}

			public override IExpect Expect(Expression<Action<TInterface>> expression)
			{
				return Mock.SetUpExpect(expression, owner.Interceptor);
			}

			public override IExpectGetter<TProperty> ExpectGet<TProperty>(Expression<Func<TInterface, TProperty>> expression)
			{
				return Mock.SetUpExpectGet(expression, owner.Interceptor);
			}

			public override IExpectSetter<TProperty> ExpectSet<TProperty>(Expression<Func<TInterface, TProperty>> expression)
			{
				return Mock.SetUpExpectSet(expression, owner.Interceptor);
			}

			public override IExpectSetter<TProperty> ExpectSet<TProperty>(Expression<Func<TInterface, TProperty>> expression, TProperty value)
			{
				return Mock.SetUpExpectSet(expression, value, owner.Interceptor);
			}

			internal override Dictionary<MethodInfo, Mock> InnerMocks
			{
				get { return owner.InnerMocks; }
				set { owner.InnerMocks = value; }
			}

			internal override Interceptor Interceptor
			{
				get { return owner.Interceptor; }
				set { owner.Interceptor = value; }
			}

			public override MockBehavior Behavior
			{
				get { return owner.Behavior; }
				internal set { owner.Behavior = value; }
			}

			public override bool CallBase
			{
				get { return owner.CallBase; }
				set { owner.CallBase = value; }
			}

			public override DefaultValue DefaultValue
			{
				get { return owner.DefaultValue; }
				set { owner.DefaultValue = value; }
			}

			public override TInterface Object
			{
				get { return owner.Object as TInterface; }
			}

			public override Mock<TNewInterface> As<TNewInterface>()
			{
				return owner.As<TNewInterface>();
			}

			public override void Verify(Expression<Action<TInterface>> expression)
			{
				Mock.Verify(expression, owner.Interceptor);
			}

			public override void Verify<TResult>(Expression<Func<TInterface, TResult>> expression)
			{
				Mock.Verify<TInterface, TResult>(expression, owner.Interceptor);
			}

			public override void VerifyGet<TProperty>(Expression<Func<TInterface, TProperty>> expression)
			{
				Mock.VerifyGet<TInterface, TProperty>(expression, owner.Interceptor);
			}

			public override void VerifySet<TProperty>(Expression<Func<TInterface, TProperty>> expression)
			{
				Mock.VerifySet<TInterface, TProperty>(expression, owner.Interceptor);
			}

			public override void VerifySet<TProperty>(Expression<Func<TInterface, TProperty>> expression, TProperty value)
			{
				Mock.VerifySet<TInterface, TProperty>(expression, value, owner.Interceptor);
			}

			public override MockedEvent<TEventArgs> CreateEventHandler<TEventArgs>()
			{
				return owner.CreateEventHandler<TEventArgs>();
			}

			public override MockedEvent<EventArgs> CreateEventHandler()
			{
				return owner.CreateEventHandler();
			}
		}

		#endregion

		// NOTE: known issue. See https://connect.microsoft.com/VisualStudio/feedback/ViewFeedback.aspx?FeedbackID=318122
		//public static implicit operator TInterface(Mock<T> mock)
		//{
		//    // TODO: doesn't work as expected but ONLY with interfaces :S
		//    return mock.Object;
		//}

		//public static explicit operator TInterface(Mock<T> mock)
		//{
		//    // TODO: doesn't work as expected but ONLY with interfaces :S
		//    throw new NotImplementedException();
		//}
	}
}