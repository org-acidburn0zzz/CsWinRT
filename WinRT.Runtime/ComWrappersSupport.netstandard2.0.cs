﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using WinRT.Interop;

namespace WinRT
{
    public static partial class ComWrappersSupport
    {
        private static ConditionalWeakTable<object, ComCallableWrapper> ComWrapperCache = new ConditionalWeakTable<object, ComCallableWrapper>();

        private static ConcurrentDictionary<IntPtr, System.WeakReference<object>> RuntimeWrapperCache = new ConcurrentDictionary<IntPtr, System.WeakReference<object>>();

        internal static InspectableInfo GetInspectableInfo(IntPtr pThis) => UnmanagedObject.FindObject<ComCallableWrapper>(pThis).InspectableInfo;

        public static object CreateRcwForComObject(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            IObjectReference identity = GetObjectReferenceForInterface(ptr).As<IUnknownVftbl>();

            object keepAliveSentinel = null;

            Func<IntPtr, System.WeakReference<object>> rcwFactory = (_) =>
            {
                object runtimeWrapper = null;
                if (identity.TryAs<IInspectable.Vftbl>(out var inspectableRef) == 0)
                {
                    var inspectable = new IInspectable(identity);
                    string runtimeClassName = inspectable.GetRuntimeClassName();
                    runtimeWrapper = TypedObjectFactoryCache.GetOrAdd(runtimeClassName, className => CreateTypedRcwFactory(className))(inspectable);
                }
                else if (identity.TryAs<ABI.WinRT.Interop.IWeakReference.Vftbl>(out var weakRef) == 0)
                {
                    runtimeWrapper = new ABI.WinRT.Interop.IWeakReference(weakRef);
                }
                keepAliveSentinel = runtimeWrapper; // We don't take a strong reference on runtimeWrapper at any point, so we need to make sure it lives until it can get assigned to rcw.
                var runtimeWrapperReference = new System.WeakReference<object>(runtimeWrapper);
                var cleanupSentinel = new RuntimeWrapperCleanup(identity.ThisPtr, runtimeWrapperReference);
                return runtimeWrapperReference;
            };

            RuntimeWrapperCache.AddOrUpdate(
                identity.ThisPtr,
                rcwFactory,
                (ptr, oldValue) =>
                {
                    if (!oldValue.TryGetTarget(out keepAliveSentinel))
                    {
                        return rcwFactory(ptr);
                    }
                    return oldValue;
                }).TryGetTarget(out object rcw);

            GC.KeepAlive(keepAliveSentinel);

            // Because .NET will de-duplicate strings and WinRT doesn't,
            // our RCW factory returns a wrapper of our string instance.
            // This ensures that our cache never sees the same managed object for two different
            // native pointers. We unwrap here to ensure that the user-experience is expected
            // and consumers get a string object for a Windows.Foundation.IReference<String>.
            // We need to do the same thing for System.Type because there can be multiple MUX.Interop.TypeName's
            // for a single System.Type.
            return rcw switch
            {
                ABI.System.Nullable<string> ns => ns.Value,
                ABI.System.Nullable<Type> nt => nt.Value,
                _ => rcw
            };
        }
    
        public static void RegisterObjectForInterface(object obj, IntPtr thisPtr)
        {
            var referenceWrapper = new System.WeakReference<object>(obj);
            var _ = new RuntimeWrapperCleanup(thisPtr, referenceWrapper);
            RuntimeWrapperCache.TryAdd(thisPtr, referenceWrapper);
        }

        // If we aren't in the activation scenario and we need to register an RCW after the fact,
        // we need to be resilient to an RCW having already been created on another thread.
        // This method registers the given object as the RCW if there isn't already one registered
        // and returns the registered RCW if it is still alive.
        public static object TryRegisterObjectForInterface(object obj, IntPtr thisPtr)
        {
            object registered = obj;
            var referenceWrapper = new System.WeakReference<object>(obj);
            RuntimeWrapperCache.AddOrUpdate(thisPtr, referenceWrapper, (_, value) =>
            {
                value.TryGetTarget(out registered);
                if (registered is null)
                {
                    registered = obj;
                }
                return value;
            });

            if (object.ReferenceEquals(registered, obj))
            {
                var _ = new RuntimeWrapperCleanup(thisPtr, referenceWrapper);
            }

            return registered;
        }

        public static IObjectReference CreateCCWForObject(object obj)
        {
            var wrapper = ComWrapperCache.GetValue(obj, _ => new ComCallableWrapper(obj));
            var objRef = ObjectReference<IUnknownVftbl>.FromAbi(wrapper.IdentityPtr);
            GC.KeepAlive(wrapper); // This GC.KeepAlive ensures that a newly created wrapper is alive until objRef is created and has AddRef'd the CCW.
            return objRef;
        }

        public static T FindObject<T>(IntPtr thisPtr)
            where T : class =>
            (T)UnmanagedObject.FindObject<ComCallableWrapper>(thisPtr).ManagedObject;

        public static unsafe IUnknownVftbl IUnknownVftbl => new IUnknownVftbl
        {
            QueryInterface = (delegate* stdcall<IntPtr, ref Guid, out IntPtr, int>)Marshal.GetFunctionPointerForDelegate(Abi_QueryInterface),
            AddRef = (delegate* stdcall<IntPtr, uint>)Marshal.GetFunctionPointerForDelegate(Abi_AddRef),
            Release = (delegate* stdcall<IntPtr, uint>)Marshal.GetFunctionPointerForDelegate(Abi_Release),
        };
        
        public static IntPtr AllocateVtableMemory(Type vtableType, int size) => Marshal.AllocCoTaskMem(size);

        private delegate int QueryInterface(IntPtr pThis, ref Guid iid, out IntPtr ptr);
        private static QueryInterface Abi_QueryInterface = Do_Abi_QueryInterface; 
        private static int Do_Abi_QueryInterface(IntPtr pThis, ref Guid iid, out IntPtr ptr)
        {
            return UnmanagedObject.FindObject<ComCallableWrapper>(pThis).QueryInterface(iid, out ptr);
        }

        private delegate uint AddRefRelease(IntPtr pThis);
        private static AddRefRelease Abi_AddRef = Do_Abi_AddRef; 
        private static uint Do_Abi_AddRef(IntPtr pThis)
        {
            return UnmanagedObject.FindObject<ComCallableWrapper>(pThis).AddRef();
        }

        private static AddRefRelease Abi_Release = Do_Abi_Release;
        private static uint Do_Abi_Release(IntPtr pThis)
        {
            return UnmanagedObject.FindObject<ComCallableWrapper>(pThis).Release();
        }

        private class RuntimeWrapperCleanup
        {
            public IntPtr _identityComObject;
            public System.WeakReference<object> _runtimeWrapper;

            public RuntimeWrapperCleanup(IntPtr identityComObject, System.WeakReference<object> runtimeWrapper)
            {
                _identityComObject = identityComObject;
                _runtimeWrapper = runtimeWrapper;
            }
            ~RuntimeWrapperCleanup()
            {
                // If runtimeWrapper is still alive, then we need to go back into the finalization queue
                // so we can check again later.
                if (_runtimeWrapper.TryGetTarget(out var _))
                {
                    GC.ReRegisterForFinalize(this);
                }
                else
                {
                    ((ICollection<KeyValuePair<IntPtr, System.WeakReference<object>>>)RuntimeWrapperCache).Remove(new KeyValuePair<IntPtr, System.WeakReference<object>>(_identityComObject, _runtimeWrapper));
                }
            }
        }
    }

    struct ComInterfaceEntry
    {
        public IntPtr Vtable;
        public Guid IID;
    }

    struct UnmanagedObject
    {
        public IntPtr _vftblPtr;
        public IntPtr _gchandlePtr;

        internal static T FindObject<T>(IntPtr thisPtr)
        {
            UnmanagedObject unmanagedObject = Marshal.PtrToStructure<UnmanagedObject>(thisPtr);
            GCHandle thisHandle = GCHandle.FromIntPtr(unmanagedObject._gchandlePtr);
            return (T)thisHandle.Target;
        }
    }

    internal class ComCallableWrapper
    {
        private Dictionary<Guid, IntPtr> _managedQITable;
        private GCHandle _qiTableHandle;
        private volatile IntPtr _strongHandle;
        private int _refs = 0;
        private GCHandle WeakHandle { get; }

        public object ManagedObject { get; }
        public ComWrappersSupport.InspectableInfo InspectableInfo { get; }
        public IntPtr IdentityPtr { get; }

        public ComCallableWrapper(object obj)
        {
            _strongHandle = IntPtr.Zero;
            WeakHandle = GCHandle.Alloc(this, GCHandleType.WeakTrackResurrection);
            ManagedObject = obj;
            var (inspectableInfo, interfaceTableEntries) = ComWrappersSupport.PregenerateNativeTypeInformation(ManagedObject);

            InspectableInfo = inspectableInfo;

            interfaceTableEntries.Add(new ComInterfaceEntry
            {
                IID = typeof(IUnknownVftbl).GUID,
                Vtable = IUnknownVftbl.AbiToProjectionVftblPtr
            });

            interfaceTableEntries.Add(new ComInterfaceEntry
            {
                IID = typeof(IInspectable).GUID,
                Vtable = IInspectable.Vftbl.AbiToProjectionVftablePtr
            });

            InitializeManagedQITable(interfaceTableEntries);

            IdentityPtr = _managedQITable[typeof(IUnknownVftbl).GUID];
        }

        ~ComCallableWrapper()
        {
            WeakHandle.Free();
            if (_managedQITable != null)
            {
                foreach (var obj in _managedQITable.Values)
                {
                    Marshal.FreeCoTaskMem(obj);
                }
                _managedQITable.Clear();
            }
            if (_qiTableHandle.IsAllocated)
            {
                _qiTableHandle.Free();
                _qiTableHandle = default;
            }
        }

        private void InitializeManagedQITable(List<ComInterfaceEntry> entries)
        {
            var managedQITable = new Dictionary<Guid, IntPtr>();
            _qiTableHandle = GCHandle.Alloc(managedQITable);
            _managedQITable = managedQITable;
            foreach (var entry in entries)
            {
                unsafe
                {
                    UnmanagedObject* ifaceTearOff = (UnmanagedObject*)Marshal.AllocCoTaskMem(sizeof(UnmanagedObject));
                    ifaceTearOff->_vftblPtr = entry.Vtable;
                    ifaceTearOff->_gchandlePtr = GCHandle.ToIntPtr(WeakHandle);
                    if (!_managedQITable.ContainsKey(entry.IID))
                    {
                        _managedQITable.Add(entry.IID, (IntPtr)ifaceTearOff);
                    }
                }
            }
        }

        internal uint AddRef()
        {
            uint refs = (uint)System.Threading.Interlocked.Increment(ref _refs);
            // We now own a reference. Let's try to create a strong handle if we don't already have one.
            if (refs == 1)
            {
                GCHandle strongHandle = GCHandle.Alloc(this);
                IntPtr previousStrongHandle = Interlocked.Exchange(ref _strongHandle, GCHandle.ToIntPtr(strongHandle));
                if (previousStrongHandle != IntPtr.Zero)
                {
                    // We've set a new handle. Release the old strong handle if there was one.
                    GCHandle.FromIntPtr(previousStrongHandle).Free();
                }
            }
            return refs;
        }

        internal uint Release()
        {
            if (_refs == 0)
            {
                throw new InvalidOperationException("WinRT wrapper has been over-released!");
            }

            IntPtr currentStrongHandle = _strongHandle;
            var refs = (uint)Interlocked.Decrement(ref _refs);
            if (refs == 0)
            {
                // No more references. We need to remove the strong reference to make sure we don't stay alive forever.
                // Only remove the strong handle if someone else doesn't change the strong handle
                // If the strong handle changes, then someone else released and re-acquired the strong handle, meaning someone is holding a reference
                IntPtr oldStrongHandle = Interlocked.CompareExchange(ref _strongHandle, IntPtr.Zero, currentStrongHandle);
                // If _refs != 0, then someone AddRef'd this back from zero
                // so we can't release this handle.
                if (oldStrongHandle == currentStrongHandle)
                {
                    GCHandle.FromIntPtr(currentStrongHandle).Free();
                }
            }
            return refs;
        }

        internal int QueryInterface(Guid iid, out IntPtr ptr)
        {
            const int E_NOINTERFACE = unchecked((int)0x80004002);
            if (ManagedObject is ICustomQueryInterface customQI)
            {
                if (customQI.GetInterface(ref iid, out ptr) == CustomQueryInterfaceResult.Handled)
                {
                    return 0;
                }
            }
            if (_managedQITable.TryGetValue(iid, out ptr))
            {
                AddRef();
                return 0;
            }
            return E_NOINTERFACE;
        }
    }
}
