using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Unmanaged;

namespace Unmanaged.Collections
{
    public unsafe struct Array<TType> where TType : class
    {
        private delegate TType CastHandler(IntPtr ptr);
        public delegate IntPtr FindHandler(TType obj);
        private delegate int SizeOfHelperHandler(Type type, bool throwIfNotMarshalable);

        private static readonly CastHandler Cast;
        public static readonly FindHandler Find;
        private static readonly SizeOfHelperHandler SizeOfHelper;

        private static readonly IntPtr _typePointer;
        private static readonly int _typeSize;
        private static readonly bool _canCreate;

        private readonly IntPtr _pointer;

        public TType Value => Cast(_pointer);

        public Array()
        {
            if (!_canCreate)
                throw new ArrayTypeMismatchException();

            _pointer = New(0);
        }

        public Array(int length)
        {
            if (!_canCreate)
                throw new ArrayTypeMismatchException();

            _pointer = New(length);

            Console.WriteLine(_pointer);
        }

        public Array(Span<int> arguments)
        {
            if (!_canCreate)
                throw new ArrayTypeMismatchException();

            _pointer = New(arguments);
        }

        private Array(TType value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));
            if (!_canCreate)
                throw new ArrayTypeMismatchException();

            _pointer = Find(value);
        }

        public static implicit operator TType(Array<TType> value)
            => value.Value;

        public static implicit operator Array<TType>(TType value)
            => new Array<TType>(value);

        public void Destroy(bool blocking = false)
        {
            Console.WriteLine(_pointer);

            if (blocking)
                Destroy(Value);
            else
                ObjectHandler.Destroy(Destroy);
        }

        public override string? ToString()
            => Value!.ToString();

        public override int GetHashCode()
            => Value!.GetHashCode();

        public override bool Equals(object? value)
            => Value!.Equals(value);

        static Array()
        {
            Cast = GenerateCastMethod();
            Find = GenerateFindMethod();
            SizeOfHelper = FindSizeOfHelperMethod();

            _typePointer = typeof(TType).TypeHandle.Value;
            _typeSize = Marshal.ReadInt32(_typePointer, sizeof(int));
            _canCreate = typeof(TType).IsArray
                && typeof(TType).GetElementType()!.IsGenericType
                && typeof(TType).GetElementType()!.GetGenericTypeDefinition() == typeof(Object<>);
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

        private static SizeOfHelperHandler FindSizeOfHelperMethod()
        {
            MethodInfo methodInfo = typeof(Marshal).GetMethod(nameof(SizeOfHelper),
                BindingFlags.NonPublic | BindingFlags.Static,
                new Type[] { typeof(Type), typeof(bool) })!;

            return (SizeOfHelperHandler)Delegate.CreateDelegate(typeof(SizeOfHelperHandler), methodInfo);
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

        private static int GetLength(Span<int> dimensions)
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

        private static void Initialize(IntPtr handle, int length, long size, Span<int> dimensions)
        {
            long startPosition = IntPtr.Size * 2;
            long endPosition = IntPtr.Size * 2 + sizeof(int);
            byte byteSize = 8;
            byte* bytes = (byte*)handle;

            for (int index = 0; index < IntPtr.Size; index++)
                bytes[index] = 0;

            for (int positionIndex = 0; startPosition < endPosition; startPosition++, positionIndex++)
                bytes[startPosition] = (byte)(length >> positionIndex * byteSize);

            if (dimensions.Length > 1)
            {
                foreach (int dimension in dimensions)
                {
                    endPosition += sizeof(int);

                    for (int positionIndex = 0; startPosition < endPosition; startPosition++, positionIndex++)
                        bytes[startPosition] = (byte)(dimension >> positionIndex * byteSize);
                }
            }

            for (long index = endPosition; index < size; index++)
                bytes[index] = 0;
        }

        private static IntPtr New(int length)
        {
            int size = GetSize(length);
            IntPtr handle = Marshal.AllocHGlobal(size);
            IntPtr pointer = handle + IntPtr.Size;

            Marshal.WriteIntPtr(pointer, _typePointer);
            IntPtr frozenSegment = ObjectHandler.RegisterFrozenSegment(handle, size);

            ObjectHandler.Objects.Add(handle, frozenSegment);

            Initialize(handle, length, size, stackalloc int[] { length });

            return pointer;
        }

        private static IntPtr New(Span<int> dimensions)
        {
            int length = GetLength(dimensions);
            int size = GetSize(length);
            IntPtr handle = Marshal.AllocHGlobal(size);
            IntPtr pointer = handle + IntPtr.Size;

            Marshal.WriteIntPtr(pointer, _typePointer);
            IntPtr frozenSegment = ObjectHandler.RegisterFrozenSegment(handle, size);

            ObjectHandler.Objects.Add(handle, frozenSegment);

            Initialize(handle, length, size, dimensions);

            return pointer;
        }
    }
}