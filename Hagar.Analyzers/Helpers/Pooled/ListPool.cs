﻿using System.Collections.Generic;

namespace Hagar.Analyzers.Helpers.Pooled
{
    internal static class ListPool<T>
    {
        private static readonly Pool<List<T>> Pool = new Pool<List<T>>(() => new List<T>(), x => x.Clear());

        public static Pool<List<T>>.Pooled Create()
        {
            return Pool.GetOrCreate();
        }
    }
}