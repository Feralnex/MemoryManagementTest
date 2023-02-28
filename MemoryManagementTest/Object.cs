using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace MemoryManagementTest
{
    public struct Object<TType>
    {
        private delegate TType CreateHandler(IntPtr ptr);
        private delegate IntPtr FindHandler(TType obj);
        private delegate int SizeOfHelperHandler(Type type, bool throwIfNotMarshalable);

        private static readonly CreateHandler Create;
        private static readonly FindHandler Find;
        private static readonly SizeOfHelperHandler SizeOfHelper;

        private static readonly IntPtr _typePointer;
        private static readonly int _typeSize;
        private static readonly ConstructorInfo[] _constructors;

        private readonly TType _value;

        public TType Value => _value;

        public Object()
        {
            _value = New();

            if (Value is IObject unmanaged)
                unmanaged.Destroyed += OnDestroyed;
        }

        public Object(params object[] arguments)
        {
            _value = New(arguments);

            if (Value is IObject unmanaged)
                unmanaged.Destroyed += OnDestroyed;
        }

        private Object(TType value)
            => _value = value;

        public static implicit operator TType(Object<TType> value)
            => value.Value;

        public static implicit operator Object<TType>(TType value)
            => new Object<TType>(value);

        public void Destroy()
        {
            if (Value is IObject unmanaged)
                unmanaged.Destroy();
            else
                Destroy(Value);
        }

        public override string? ToString()
            => Value!.ToString();

        public override int GetHashCode()
            => Value!.GetHashCode();

        public override bool Equals(object? value)
            => Value!.Equals(value);

        private void OnDestroyed()
        {
            IObject unmanaged = (IObject)Value!;

            unmanaged.Destroyed -= OnDestroyed;

            Destroy(Value);
        }

        static Object()
        {
            _typePointer = typeof(TType).TypeHandle.Value;
            _typeSize = Marshal.ReadInt32(_typePointer, sizeof(int));
            _constructors = typeof(TType).GetConstructors(BindingFlags.Instance | BindingFlags.Public);

            DynamicMethod method = new DynamicMethod(nameof(Create), typeof(TType), new Type[] { typeof(IntPtr) }, true);
            ILGenerator generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ret);

            Create = (CreateHandler)method.CreateDelegate(typeof(CreateHandler));

            method = new DynamicMethod(nameof(Find), typeof(IntPtr), new Type[] { typeof(TType) }, true);
            generator = method.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ret);

            Find = (FindHandler)method.CreateDelegate(typeof(FindHandler));

            MethodInfo methodInfo = typeof(Marshal).GetMethod(nameof(SizeOfHelper),
                BindingFlags.NonPublic | BindingFlags.Static,
                new Type[] { typeof(Type), typeof(bool) })!;

            SizeOfHelper = (SizeOfHelperHandler)Delegate.CreateDelegate(typeof(SizeOfHelperHandler), methodInfo);
        }

        private static void Destroy(TType obj)
        {
            IntPtr pointer = Find(obj);
            IntPtr handle = pointer - IntPtr.Size;

            Marshal.FreeHGlobal(handle);
        }

        private static bool DoTypesMatch(object[] arguments, ParameterInfo[] parameters, out int bestFitCount)
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

        private static unsafe void Initialize(IntPtr handle, int length, long size, int[] dimensions)
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

        private static TType New(params object[] arguments)
        {
            if (typeof(TType).IsArray)
            {
                if (!typeof(TType).GetElementType()!.IsGenericType
                    || (typeof(TType).GetElementType()!.IsGenericType
                    && typeof(TType).GetElementType()!.GetGenericTypeDefinition() != typeof(Object<>)))
                    throw new ArrayTypeMismatchException();

                int[] dimensions = GetDimensions(arguments);

                return New(dimensions);
            }

            int size = _typeSize + IntPtr.Size;
            IntPtr handle = Marshal.AllocHGlobal(size);
            IntPtr pointer = handle + IntPtr.Size;

            Marshal.WriteIntPtr(pointer, _typePointer);

            TType value = Create(pointer);

            if (!TryGet(arguments, out ConstructorInfo? constructor))
                throw new ArgumentException();

            constructor?.Invoke(value, arguments);

            return value;
        }

        private static TType New(int[] dimensions)
        {
            int length = GetLength(dimensions);
            int size = GetSize(length);
            IntPtr handle = Marshal.AllocHGlobal(size);
            IntPtr pointer = handle + IntPtr.Size;

            Marshal.WriteIntPtr(pointer, _typePointer);

            TType value = Create(pointer);

            Initialize(handle, length, size, dimensions);

            return value;
        }

        private static bool TryGet(object[] arguments, out ConstructorInfo? constructor)
        {
            int bestFitIndex = -1;
            int bestFitCount = 0;
            ParameterInfo[] parameters;

            constructor = default;

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
                        {
                            constructor = _constructors[bestFitIndex];

                            return true;
                        }
                    }
                }
            }

            if (bestFitIndex < 0)
                return false;

            constructor = _constructors[bestFitIndex];

            return true;
        }

        private static int[] GetDimensions(params object[] arguments)
        {
            TypeConverter converter;

            if (arguments.Length < 1)
                throw new ArgumentException();

            int[] dimensions = new int[arguments.Length];

            for (int index = 0; index < arguments.Length; index++)
            {
                object argument = arguments[index];

                if (argument is null)
                    throw new ArgumentNullException(nameof(argument));
                if (!argument.GetType().IsValueType)
                    throw new ArgumentException();

                converter = TypeDescriptor.GetConverter(argument.GetType());

                if (!converter.CanConvertTo(typeof(long)))
                    throw new ArgumentException();

                long dimension = (long)converter.ConvertTo(argument, typeof(long))!;

                if (dimension > Array.MaxLength)
                    throw new OutOfMemoryException();

                dimensions[index] = (int)dimension;
            }

            return dimensions;
        }

        private static int GetElementSize(Type type)
        {
            if (type.IsValueType)
            {
                if (type.IsGenericType)
                    return SizeOfHelper(type, true);

                return Marshal.SizeOf(type);
            }
            else
                return IntPtr.Size;
        }

        private static int GetLength(int[] dimensions)
        {
            long length = dimensions[0];

            for (int index = 1; index < dimensions.Length; index++)
                length *= dimensions[index];

            if (length > Array.MaxLength)
                throw new OutOfMemoryException();

            return (int)length;
        }

        private static int GetSize(int length)
        {
            Type elementType = typeof(TType).GetElementType()!;
            long elementSize = GetElementSize(elementType);
            long size = elementSize * length + _typeSize + IntPtr.Size;

            if (size > int.MaxValue)
                throw new OutOfMemoryException();

            return (int)size;
        }
    }
}