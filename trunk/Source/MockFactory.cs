﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Moq
{
	/// <summary>
	/// Utility factory class to use to construct multiple 
	/// mocks when consistent behavior and verification is 
	/// desired for all of them.
	/// </summary>
	/// <remarks>
	/// If multiple mocks will be created during a test, passing 
	/// the desired <see cref="MockBehavior"/> (if different than the 
	/// <see cref="MockBehavior.Default"/>) and later verifying each
	/// mock can become repetitive and tedious.
	/// <para>
	/// This factory class helps in that scenario by providing a 
	/// disposable object that simplifies creation of multiple 
	/// mocks with the exact same <see cref="MockBehavior"/> and 
	/// posterior verification (which happens at factory dispose 
	/// time).
	/// </para>
	/// </remarks>
	/// <example>
	/// The following is a straightforward example on how to 
	/// create and automatically verify strict mocks:
	/// <code>
	/// using (var factory = new MockFactory(MockBehavior.Strict, MockVerification.All))
	/// {
	/// 	var foo = factory.Create&lt;IFoo&gt;();
	/// 	var bar = factory.Create&lt;IBar&gt;();
	/// 
	///		// no need to call Verifiable() on the expectation 
	///		// as we'll be validating all expectations anyway.
	/// 	foo.Expect(f => f.Do());
	/// 	bar.Expect(b => b.Redo());
	/// 
	///		// exercise the mocks here
	/// } 
	/// // At this point all expectations are already checked 
	/// // and an optional MockException might be thrown. 
	/// // Note also that because the mocks are strict, any invocation 
	/// // that doesn't have a matching expectation will also throw a MockException.
	/// </code>
	/// The following examples shows how to setup the factory 
	/// to create loose mocks that are automatically verified 
	/// (only for their verifiable expectations) when the test 
	/// finishes:
	/// <code>
	/// using (var factory = new MockFactory(MockBehavior.Loose, MockVerification.Verifiable))
	/// {
	/// 	var foo = factory.Create&lt;IFoo&gt;();
	/// 	var bar = factory.Create&lt;IBar&gt;();
	/// 
	///     // this expectation will be verified at the end of the "using" block
	/// 	foo.Expect(f => f.Do()).Verifiable();
	/// 	
	///     // this expectation will NOT be verified 
	/// 	foo.Expect(f => f.Calculate());
	/// 	
	///     // this expectation will be verified at the end of the "using" block
	/// 	bar.Expect(b => b.Redo()).Verifiable();
	/// 
	///		// exercise the mocks here
	///		// note that because the mocks are Loose, members 
	///		// called in the interfaces for which no matching
	///		// expectations exist will NOT throw exceptions, 
	///		// and will rather return default values.
	///		
	/// } 
	/// // At this point verifiable expectations are already checked 
	/// // and an optional MockException might be thrown.
	/// </code>
	/// </example>
	/// <seealso cref="MockVerification"/>
	/// <seealso cref="MockBehavior"/>
	public sealed class MockFactory : IDisposable
	{
		List<IVerifiable> mocks = new List<IVerifiable>();
		MockBehavior behavior;
		MockVerification verification;

		/// <summary>
		/// Initializes the factory with the given <paramref name="behavior"/> 
		/// and <paramref name="verification"/> parameters for newly 
		/// created mocks from the factory.
		/// </summary>
		/// <param name="behavior">The behavior to use for mocks created 
		/// using the <see cref="Create{T}()"/> factory method</param>
		/// <param name="verification">How to verify mocks when the factory is disposed.</param>
		public MockFactory(MockBehavior behavior, MockVerification verification)
		{
			this.behavior = behavior;
			this.verification = verification;
		}

		/// <summary>
		/// Creates a new mock with the <see cref="MockBehavior"/> specified 
		/// at factory construction time.
		/// </summary>
		/// <typeparam name="T">Type to mock.</typeparam>
		/// <returns>A new <see cref="Mock{T}"/>.</returns>
		/// <example>
		/// <code>
		/// using (var factory = new MockFactory(MockBehavior.Relaxed, MockVerification.Verifiable))
		/// {
		/// 	var foo = factory.Create&lt;IFoo&gt;();
		/// 	// use mock on tests
		/// }
		/// </code>
		/// </example>
		public Mock<T> Create<T>()
			where T : class
		{
			var mock = new Mock<T>(behavior);
			mocks.Add(mock);

			return mock;
		}

		/// <summary>
		/// Creates a new mock with the <see cref="MockBehavior"/> specified 
		/// at factory construction time and with the 
		/// the given constructor arguments for the class.
		/// </summary>
		/// <remarks>
		/// The mock will try to find the best match constructor given the constructor arguments, and invoke that 
		/// to initialize the instance. This applies only to classes, not interfaces.
		/// </remarks>
		/// <typeparam name="T">Type to mock.</typeparam>
		/// <returns>A new <see cref="Mock{T}"/>.</returns>
		/// <example>
		/// <code>
		/// using (var factory = new MockFactory(MockBehavior.Default, MockVerification.Verifiable))
		/// {
		/// 	var mock = factory.Create&lt;MyBase&gt;("Foo", 25, true);
		/// 	// use mock on tests
		/// }
		/// </code>
		/// </example>
		public Mock<T> Create<T>(params object[] args)
			where T : class
		{
			var mock = new Mock<T>(behavior, args);
			mocks.Add(mock);

			return mock;
		}

		/// <summary>
		/// Disposes the factory, optionally causing all mocks 
		/// to be verified according to the <see cref="MockVerification"/> 
		/// behavior specified at construction time.
		/// </summary>
		public void Dispose()
		{
			if (verification == MockVerification.None) return;

			Action<IVerifiable> verify;
			if (verification == MockVerification.All)
				verify = mock => mock.VerifyAll();
			else
				verify = mock => mock.Verify();

			StringBuilder message = new StringBuilder();

			foreach (var mock in mocks)
			{
				try
				{
					verify(mock);
				}
				catch (MockVerificationException mve)
				{
					message.AppendLine(mve.GetRawExpectations());
				}
			}

			if (message.ToString().Length > 0)
				throw new MockException(MockException.ExceptionReason.VerificationFailed,
					String.Format(Properties.Resources.VerficationFailed, message));
		}
	}
}
