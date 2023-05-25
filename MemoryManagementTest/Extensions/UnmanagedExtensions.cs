using System;
using System.Collections.Generic;
using Unmanaged.Collections;

namespace Unmanaged.Extensions
{
    public static class UnmanagedExtensions
    {
        public static void Destroy<TType>(this TType value, bool blocking = false) where TType : class
        {
            Type type = typeof(TType);
            Type genericType = typeof(Object<>);

            if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
            {
                Object<TType> unmanagedValue = (Object<TType>)value;

                unmanagedValue.Destroy(blocking);
            }
            else if (type.IsArray && type.GetElementType()!.IsGenericType
                && type.GetElementType()!.GetGenericTypeDefinition() == genericType)
            {
                Array<TType> unmanagedValue = (Array<TType>)value;

                unmanagedValue.Destroy(blocking);
            }
        }

        public static void CopyTo<TType>(this IEnumerable<TType> source, Object<TType>[] destination, int index = 0)
        {
            foreach (TType item in source)
            {
                destination[index++] = item;
            }
        }
    }
}
