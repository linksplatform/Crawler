﻿using System;

#if NET45
using System.Threading;

#pragma warning disable CS0168 // The variable 'ex' is declared but never used
#endif

namespace Platform.Helpers.Threading
{
    public static class ThreadHelpers
    {
        public static void SyncInvokeWithExtendedStack<T>(T param, Action<object> action, int maxStackSize = 200 * 1024 * 1024)
        {
#if NET45
#if DEBUG
            var thread = new Thread(obj =>
            {
                try
                {
                    action(obj);
                }
                catch (Exception ex)
                {
                    // TODO: Log exception
                }
            }, maxStackSize);
#else
            var thread = new Thread(new ParameterizedThreadStart(action), maxStackSize);
#endif
            thread.Start(param);
            thread.Join();
#else
            action(param);
#endif
        }

        public static void SyncInvokeWithExtendedStack(Action action, int maxStackSize = 200 * 1024 * 1024)
        {
#if NET45
#if DEBUG
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // TODO: Log exception
                }
            }, maxStackSize);
#else
            var thread = new Thread(new ThreadStart(action), maxStackSize);
#endif
            thread.Start();
            thread.Join();
#else
            action();
#endif
        }

<<<<<<< HEAD
#if NET45
=======
>>>>>>> 9fe309aeebc832ff3656bd5c8f2f766b327b6ab5
        public static Thread StartNew<T>(T param, Action<object> action)
        {
#if DEBUG
            var thread = new Thread(obj =>
            {
                try
                {
                    action(obj);
                }
                catch (Exception ex)
                {
                    // TODO: Log exception
                }
            });
#else
            var thread = new Thread(new ParameterizedThreadStart(action));
#endif
            thread.Start(param);
            return thread;
        }

        public static Thread StartNew(Action action)
        {
#if DEBUG
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // TODO: Log exception
                }
            });
#else
            var thread = new Thread(new ThreadStart(action));
#endif
            thread.Start();
            return thread;
        }
<<<<<<< HEAD
#endif
=======
>>>>>>> 9fe309aeebc832ff3656bd5c8f2f766b327b6ab5
    }
}
