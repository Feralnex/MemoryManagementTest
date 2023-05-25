using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System;
using Unmanaged.Interfaces;
using Unmanaged.Collections;
using Unmanaged.Extensions;
using System.Collections.Generic;

namespace Unmanaged
{
    public unsafe struct Object<TType>
    {
        private delegate TType CastHandler(IntPtr ptr);
        private delegate IntPtr FindHandler(TType obj);

        private static readonly CastHandler Cast;
        private static readonly FindHandler Find;
        private static readonly MethodInfo get_Item;

        private static readonly IntPtr _typePointer;
        private static readonly int _typeSize;
        private static readonly bool _canCreate;
        private static readonly ConstructorInfo[] _constructors;
        private static readonly ConstructorHandler[] _constructorHandlers;
        private static readonly Dictionary<Object<Type>[], ConstructorHandler> _constructorCache;

        private readonly IntPtr _pointer;

        public TType Value => Cast(_pointer);

        public Object()
        {
            if (!_canCreate)
                throw new ArgumentException();

            _pointer = New(Span<Object<object>>.Empty);

            if (Value is IObject unmanaged)
                unmanaged.Destroyed += OnDestroyed;

            Console.WriteLine(_pointer);
        }

        public Object(Span<Object<object>> arguments)
        {
            if (!_canCreate)
                throw new ArgumentException();

            _pointer = New(arguments);

            if (Value is IObject unmanaged)
                unmanaged.Destroyed += OnDestroyed;
        }

        private Object(TType value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (!_canCreate)
                throw new ArgumentException();
            if (typeof(TType).IsValueType)
                throw new ArgumentException();

            _pointer = Find(value);
        }

        public static implicit operator TType(Object<TType> value)
            => value.Value;

        public static implicit operator Object<TType>(TType value)
            => new Object<TType>(value);

        public void Destroy(bool blocking = false)
        {
            Console.WriteLine(_pointer);

            if (Value is IObject unmanaged)
                unmanaged.Destroy(blocking);
            else
            {
                if (blocking)
                    Destroy(Value);
                else
                    ObjectHandler.Destroy(Destroy);
            }
        }

        public override string? ToString()
            => Value!.ToString();

        public override int GetHashCode()
            => Value!.GetHashCode();

        public override bool Equals(object? value)
            => Value!.Equals(value);

        private void OnDestroyed(bool blocking)
        {
            IObject unmanaged = (IObject)Value!;

            unmanaged.Destroyed -= OnDestroyed;

            if (blocking)
                Destroy(Value);
            else
                ObjectHandler.Destroy(Destroy);
        }

        static Object()
        {
            Cast = GenerateCastMethod();
            Find = GenerateFindMethod();
            get_Item = typeof(Span<object>).GetMethod(nameof(get_Item))!;

            _typePointer = typeof(TType).TypeHandle.Value;
            _typeSize = Marshal.ReadInt32(_typePointer, sizeof(int));
            _canCreate = !typeof(TType).IsArray;
            _constructors = typeof(TType).GetConstructors(BindingFlags.Instance | BindingFlags.Public);
            _constructorHandlers = GenerateConstructorHandlers(_constructors);
            _constructorCache = GenerateConstructorCache(_constructors, _constructorHandlers);
        }

        private void Destroy() =>
            Destroy(Value);

        private static void Destroy(TType? value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            IntPtr pointer = Find(value);
            IntPtr handle = pointer - IntPtr.Size;
            IntPtr frozenSegment = ObjectHandler.Objects[handle];

            ObjectHandler.Objects.Remove(handle);

            ObjectHandler.UnregisterFrozenSegment(frozenSegment);
            Marshal.FreeHGlobal(handle);
        }

        private static bool DoTypesMatch(Span<Object<object>> arguments, ParameterInfo[] parameters, out int bestFitCount)
        {
            Type argumentType;
            Type parameterType;
            TypeConverter converter;

            bestFitCount = 0;

            for (int index = 0; index < arguments.Length; index++)
            {
                argumentType = arguments[index].GetType();
                parameterType = parameters[index].ParameterType;

                if (argumentType != parameterType)
                {
                    if (argumentType.IsValueType)
                    {
                        converter = TypeDescriptor.GetConverter(argumentType);

                        if (!converter.CanConvertTo(parameterType))
                            return false;
                    }
                    else if (!parameters[index].ParameterType.IsAssignableFrom(arguments[index].GetType()))
                        return false;
                }
                else
                    bestFitCount++;
            }

            return true;
        }

        private static ConstructorHandler[] GenerateConstructorHandlers(ConstructorInfo[] constructors)
        {
            ConstructorHandler[] constructorHandlers = new ConstructorHandler[_constructors.Length];

            for (int index = 0; index < constructorHandlers.Length; index++)
                constructorHandlers[index] = GenerateConstructorHandler(constructors[index]);

            return constructorHandlers;
        }

        private static Dictionary<Object<Type>[], ConstructorHandler> GenerateConstructorCache(ConstructorInfo[] constructors, ConstructorHandler[] constructorHandlers)
        {
            Dictionary<Object<Type>[], ConstructorHandler> constructorCache = new Dictionary<Object<Type>[], ConstructorHandler>();
            ParameterInfo[] parameters;
            Object<Type>[] types;

            for (int index = 0; index < constructors.Length; index++)
            {
                parameters = constructors[index].GetParameters();
                types = new Array<Object<Type>[]>(parameters.Length);

                for (int parameterIndex = 0; parameterIndex < types.Length; parameterIndex++)
                    types[parameterIndex] = parameters[parameterIndex].GetType();

                constructorCache.Add(types, constructorHandlers[index]);
            }

            return constructorCache;
        }

        private static ConstructorHandler GenerateConstructorHandler(ConstructorInfo constructor)
        {
            DynamicMethod dynamicMethod = new DynamicMethod(nameof(ConstructorHandler), typeof(void), new[] { typeof(object), typeof(Span<Object<object>>) }, true);
            ILGenerator generator = dynamicMethod.GetILGenerator();
            ParameterInfo[] parameters = constructor.GetParameters();
            Type? parameterType;

            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ldarg_0); // Load first argument onto the stack.
            for (int index = 0; index < parameters.Length; index++)
            {
                generator.Emit(OpCodes.Ldarga_S, 1); // Load second argument address onto the stack.
                generator.Emit(OpCodes.Ldc_I4, index); // Specify index of the collection.
                generator.Emit(OpCodes.Call, get_Item); // Call '[]' on Span.
                generator.Emit(OpCodes.Ldind_Ref); // Load object's reference onto the stack from [index]

                // Unbox the parameter value if necessary.
                parameterType = parameters[index].ParameterType;
                if (parameterType.IsValueType)
                    generator.Emit(OpCodes.Unbox_Any, parameterType); // Unbox object to the parameter type
                else
                    generator.Emit(OpCodes.Castclass, parameterType); // Cast object to the parameter type
            }
            generator.Emit(OpCodes.Call, constructor); // Call the constructor to initialize the object.
            generator.Emit(OpCodes.Nop);
            generator.Emit(OpCodes.Ret); // Return from the dynamic method.

            return (ConstructorHandler)dynamicMethod.CreateDelegate(typeof(ConstructorHandler));
        }

        private static CastHandler GenerateCastMethod()
        {
            DynamicMethod method = new DynamicMethod(nameof(Cast), typeof(TType), new Type[] { typeof(IntPtr) }, true);
            ILGenerator generator = method.GetILGenerator();
            // Load the current parameter value onto the stack.
            generator.Emit(OpCodes.Ldarg_0);
            // Return from the dynamic method.
            generator.Emit(OpCodes.Ret);

            return (CastHandler)method.CreateDelegate(typeof(CastHandler));
        }

        private static FindHandler GenerateFindMethod()
        {
            DynamicMethod method = new DynamicMethod(nameof(Find), typeof(IntPtr), new Type[] { typeof(TType) }, true);
            ILGenerator generator = method.GetILGenerator();
            // Load the current parameter value onto the stack.
            generator.Emit(OpCodes.Ldarg_0);
            // Return from the dynamic method.
            generator.Emit(OpCodes.Ret);

            return (FindHandler)method.CreateDelegate(typeof(FindHandler));
        }

        private static void Initialize(IntPtr handle, int length, long size, Object<int>[] dimensions)
        {
            long startPosition = IntPtr.Size * 2;
            long endPosition = IntPtr.Size * 2 + sizeof(long);
            byte byteSize = 8;
            byte* bytes = (byte*)handle;

            for (int index = 0; index < IntPtr.Size; index++)
                bytes[index] = 0;

            for (int positionIndex = 0; startPosition < endPosition; startPosition++, positionIndex++)
                bytes[startPosition] = (byte)(length >> positionIndex * byteSize);

            foreach (int dimension in dimensions)
            {
                endPosition += sizeof(int);

                for (int positionIndex = 0; startPosition < endPosition; startPosition++, positionIndex++)
                    bytes[startPosition] = (byte)(dimension >> positionIndex * byteSize);
            }

            for (long index = endPosition; index < size; index++)
                bytes[index] = 0;
        }

        private static IntPtr New(Span<Object<object>> arguments)
        {
            int size = _typeSize + IntPtr.Size;
            IntPtr handle = Marshal.AllocHGlobal(size);
            IntPtr pointer = handle + IntPtr.Size;

            Marshal.WriteIntPtr(pointer, _typePointer);
            IntPtr frozenSegment = ObjectHandler.RegisterFrozenSegment(handle, size);

            ObjectHandler.Objects.Add(handle, frozenSegment);

            TType value = Cast(pointer);

            if (!TryGet(arguments, out ConstructorHandler? constructor))
                throw new ArgumentException();

            constructor?.Invoke(value, arguments);

            return pointer;
        }

        private static bool TryGet(Span<Object<object>> arguments, out ConstructorHandler? constructor)
        {
            Object<Type>[] types = new Array<Object<Type>[]>(arguments.Length);

            for (int index = 0; index < types.Length; index++)
                types[index] = arguments[index].GetType();

            if (TryGetFromCache(types, out constructor))
            {
                types.Destroy();

                return true;
            }

            if (TryGet(arguments, out int bestFitIndex))
            {
                constructor = _constructorHandlers[bestFitIndex];

                _constructorCache.Add(types, constructor);

                return true;
            }

            types.Destroy();

            return false;
        }

        private static bool TryGet(Span<Object<object>> arguments, out int bestFitIndex)
        {
            int bestFitCount = 0;
            ConstructorInfo? constructor;
            ParameterInfo[] parameters;

            bestFitIndex = -1;

            for (int index = 0; index < _constructors.Length; index++)
            {
                constructor = _constructors[index];
                parameters = constructor.GetParameters();

                if (arguments.Length == parameters.Length
                    && DoTypesMatch(arguments, parameters, out int currentBestFitCount))
                {
                    if (bestFitIndex < 0
                        || bestFitCount < currentBestFitCount)
                    {
                        bestFitIndex = index;
                        bestFitCount = currentBestFitCount;

                        if (bestFitCount == arguments.Length)
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetFromCache(Object<Type>[] types, out ConstructorHandler? constructor)
        {
            constructor = default;

            if (_constructorCache.TryGetValue(types, out ConstructorHandler? constructorInfo))
            {
                constructor = constructorInfo;

                return true;
            }

            return false;
        }
    }
}