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
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Castle.Core.Interceptor;
using Moq.Language;
using Moq.Language.Flow;

namespace Moq
{
	internal class MethodCall : IProxyCall, IExpect
	{
		protected MethodInfo method;
		Expression originalExpression;
		Exception exception;
		Action<object[]> callback;
		List<IMatcher> argumentMatchers = new List<IMatcher>();
		int callCount;
		bool isOnce;
		MockedEvent mockEvent;
		Delegate mockEventArgsFunc;
		int? expectedCallCount = null;
		List<KeyValuePair<int, Expression>> outValues = new List<KeyValuePair<int, Expression>>();

		public bool IsVerifiable { get; set; }
		public bool IsNever { get; set; }
		public bool Invoked { get; set; }
		public Expression ExpectExpression { get { return originalExpression; } }

		public MethodCall(Expression originalExpression, MethodInfo method, params Expression[] arguments)
		{
			this.originalExpression = originalExpression;
			this.method = method;

			var parameters = method.GetParameters();
			for (int i = 0; i < parameters.Length; i++)
			{
				var parameter = parameters[i];
				var argument = arguments[i];
				if (parameter.IsOut)
				{
					outValues.Add(new KeyValuePair<int, Expression>(i, argument));
				}
				else if (parameter.ParameterType.IsByRef)
				{
					var value = argument.PartialEval();
					if (value.NodeType == ExpressionType.Constant)
						argumentMatchers.Add(new RefMatcher(((ConstantExpression)value).Value));
					else
						throw new NotSupportedException();
				}
				else
				{
					argumentMatchers.Add(MatcherFactory.CreateMatcher(argument));
				}
			}
		}

		public void SetOutParameters(IInvocation call)
		{
			foreach (var item in outValues)
			{
				var value = item.Value.PartialEval();
				if (value.NodeType == ExpressionType.Constant)
					call.SetArgumentValue(item.Key, ((ConstantExpression)value).Value);
				else
					throw new NotSupportedException();
			}
		}

		public virtual bool Matches(IInvocation call)
		{
			var args = new List<object>();
			var parameters = call.Method.GetParameters();
			for (int i = 0; i < parameters.Length; i++)
			{
				if (!parameters[i].IsOut)
					args.Add(call.Arguments[i]);
			}

			if (IsEqualMethodOrOverride(call) &&
				argumentMatchers.Count == args.Count)
			{
				for (int i = 0; i < argumentMatchers.Count; i++)
				{
					if (!argumentMatchers[i].Matches(args[i]))
						return false;
				}

				return true;
			}

			return false;
		}

		public virtual void Execute(IInvocation call)
		{
			Invoked = true;

			if (callback != null)
				callback(call.Arguments);

			if (exception != null)
				throw exception;

			callCount++;

			if (isOnce && callCount > 1)
				throw new MockException(MockException.ExceptionReason.MoreThanOneCall,
					String.Format(Properties.Resources.MoreThanOneCall,
					call.Format()));


			if (IsNever)
				throw new MockException(MockException.ExceptionReason.ExpectedNever,
					String.Format(Properties.Resources.ExpectedNever,
					call.Format()));


			if (expectedCallCount.HasValue && callCount > expectedCallCount)
				throw new MockException(MockException.ExceptionReason.MoreThanNCalls,
					String.Format(Properties.Resources.MoreThanNCalls, expectedCallCount,
					call.Format()));


			if (mockEvent != null)
			{
				var argsFuncType = mockEventArgsFunc.GetType();

				if (argsFuncType.IsGenericType &&
					argsFuncType.GetGenericArguments().Length == 1)
				{
					mockEvent.DoRaise((EventArgs)mockEventArgsFunc.InvokePreserveStack());
				}
				else
				{
					mockEvent.DoRaise((EventArgs)mockEventArgsFunc.InvokePreserveStack(call.Arguments));
				}
			}
		}

		public IThrowsResult Throws(Exception exception)
		{
			this.exception = exception;
			return this;
		}

		public IThrowsResult Throws<TException>() 
			where TException : Exception, new()
		{
			this.exception = new TException();
			return this;
		}

		public ICallbackResult Callback(Action callback)
		{
			SetCallbackWithoutArguments(callback);
			return this;
		}

		public ICallbackResult Callback<T>(Action<T> callback)
		{
			SetCallbackWithArguments(callback);
			return this;
		}

		public ICallbackResult Callback<T1, T2>(Action<T1, T2> callback)
		{
			SetCallbackWithArguments(callback);
			return this;
		}

		public ICallbackResult Callback<T1, T2, T3>(Action<T1, T2, T3> callback)
		{
			SetCallbackWithArguments(callback);
			return this;
		}

		public ICallbackResult Callback<T1, T2, T3, T4>(Action<T1, T2, T3, T4> callback)
		{
			SetCallbackWithArguments(callback);
			return this;
		}

		protected virtual void SetCallbackWithoutArguments(Action callback)
		{
			this.callback = delegate { callback(); };
		}

		protected virtual void SetCallbackWithArguments(Delegate callback)
		{
			this.callback = delegate(object[] args) { callback.InvokePreserveStack(args); };
		}

		public void Verifiable()
		{
			IsVerifiable = true;
		}

		private bool IsEqualMethodOrOverride(IInvocation call)
		{
			return call.Method == method ||
				(call.Method.DeclaringType.IsClass &&
				call.Method.IsVirtual &&
				call.Method.GetBaseDefinition() == method);
		}

		public IExtensible AtMostOnce()
		{
			isOnce = true;

			return this;
		}

		public void Never()
		{
			IsNever = true;
		}

		public IExtensible AtMost( int callCount )
		{
			expectedCallCount = callCount;

			return this;
		}

		public IExtensible Raises(MockedEvent eventHandler, EventArgs args)
		{
			Guard.ArgumentNotNull(args, "args");

			return RaisesImpl(eventHandler, (Func<EventArgs>)(() => args));
		}

		public IExtensible Raises(MockedEvent eventHandler, Func<EventArgs> func)
		{
			return RaisesImpl(eventHandler, func);
		}

		public IExtensible Raises<T>(MockedEvent eventHandler, Func<T, EventArgs> func)
		{
			return RaisesImpl(eventHandler, func);
		}

		public IExtensible Raises<T1, T2>(MockedEvent eventHandler, Func<T1, T2, EventArgs> func)
		{
			return RaisesImpl(eventHandler, func);
		}

		public IExtensible Raises<T1, T2, T3>(MockedEvent eventHandler, Func<T1, T2, T3, EventArgs> func)
		{
			return RaisesImpl(eventHandler, func);
		}

		public IExtensible Raises<T1, T2, T3, T4>(MockedEvent eventHandler, Func<T1, T2, T3, T4, EventArgs> func)
		{
			return RaisesImpl(eventHandler, func);
		}

		private IExtensible RaisesImpl(MockedEvent eventHandler, Delegate func)
		{
			Guard.ArgumentNotNull(eventHandler, "eventHandler");
			Guard.ArgumentNotNull(func, "func");

			mockEvent = eventHandler;
			mockEventArgsFunc = func;

			return this;
		}
	}
}
