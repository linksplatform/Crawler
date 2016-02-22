// from .NET 4.5.2 source
// ==++==
// 
//   Copyright (c) Microsoft Corporation.  All rights reserved.
// 
// ==--==
/*============================================================
**
** Class:  Dictionary
** 
** <OWNER>[....]</OWNER>
**
** Purpose: Generic hash table implementation
**
** #DictionaryVersusHashtableThreadSafety
** Hashtable has multiple reader/single writer (MR/SW) thread safety built into 
** certain methods and properties, whereas Dictionary doesn't. If you're 
** converting framework code that formerly used Hashtable to Dictionary, it's
** important to consider whether callers may have taken a dependence on MR/SW
** thread safety. If a reader writer lock is available, then that may be used
** with a Dictionary to get the same thread safety guarantee. 
** 
** Reader writer locks don't exist in silverlight, so we do the following as a
** result of removing non-generic collections from silverlight: 
** 1. If the Hashtable was fully synchronized, then we replace it with a 
**    Dictionary with full locks around reads/writes (same thread safety
**    guarantee).
** 2. Otherwise, the Hashtable has the default MR/SW thread safety behavior, 
**    so we do one of the following on a case-by-case basis:
**    a. If the ---- can be addressed by rearranging the code and using a temp
**       variable (for example, it's only populated immediately after created)
**       then we address the ---- this way and use Dictionary.
**    b. If there's concern about degrading performance with the increased 
**       locking, we ifdef with FEATURE_NONGENERIC_COLLECTIONS so we can at 
**       least use Hashtable in the desktop build, but Dictionary with full 
**       locks in silverlight builds. Note that this is heavier locking than 
**       MR/SW, but this is the only option without rewriting (or adding back)
**       the reader writer lock. 
**    c. If there's no performance concern (e.g. debug-only code) we 
**       consistently replace Hashtable with Dictionary plus full locks to 
**       reduce complexity.
**    d. Most of serialization is dead code in silverlight. Instead of updating
**       those Hashtable occurences in serialization, we carved out references 
**       to serialization such that this code doesn't need to build in 
**       silverlight. 
===========================================================*/

using System.Collections.Generic;

namespace Platform.Helpers.Collections1 {
    using System;
    using System.Collections;
    using System.Diagnostics;

    //[DebuggerTypeProxy(typeof(Mscorlib_DictionaryDebugView<,>))]
    [DebuggerDisplay("Count = {Count}")]
    [System.Runtime.InteropServices.ComVisible(false)]
    public class UnsafeDictionary<TKey,TValue>: IDictionary<TKey,TValue>, IDictionary, IReadOnlyDictionary<TKey, TValue>  {
    
        public struct Entry {
            public int hashCode;    // Lower 31 bits of hash code, -1 if unused
            public int next;        // Index of next entry, -1 if last
            public TKey key;           // Key of entry
            public TValue value;         // Value of entry
        }

        private int[] buckets;
        public Entry[] entries;
        private int count;
        private int version;
        private int freeList;
        private int freeCount;
        private IEqualityComparer<TKey> comparer;
        private KeyCollection keys;
        private ValueCollection values;
        private Object _syncRoot;

        public UnsafeDictionary(): this(0, null) {}

        public UnsafeDictionary(int capacity): this(capacity, null) {}

        public UnsafeDictionary(IEqualityComparer<TKey> comparer): this(0, comparer) {}

        public UnsafeDictionary(int capacity, IEqualityComparer<TKey> comparer) {
            //if (capacity < 0) ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            if (capacity > 0) Initialize(capacity);
            this.comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        public UnsafeDictionary(IDictionary<TKey,TValue> dictionary): this(dictionary, null) {}

        public UnsafeDictionary(IDictionary<TKey,TValue> dictionary, IEqualityComparer<TKey> comparer):
            this(dictionary != null? dictionary.Count: 0, comparer) {

            //if( dictionary == null) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
            //}

            foreach (var pair in dictionary) {
                Add(pair.Key, pair.Value);
            }
        }
            
        public IEqualityComparer<TKey> Comparer {
            get {
                return comparer;                
            }               
        }
        
        public int Count {
            get { return count - freeCount; }
        }

        public KeyCollection Keys {
            get {
                //Contract.Ensures(Contract.Result<KeyCollection>() != null);
                if (keys == null) keys = new KeyCollection(this);
                return keys;
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys {
            get {                
                if (keys == null) keys = new KeyCollection(this);                
                return keys;
            }
        }

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys {
            get {                
                if (keys == null) keys = new KeyCollection(this);                
                return keys;
            }
        }

        public ValueCollection Values {
            get {
                //Contract.Ensures(Contract.Result<ValueCollection>() != null);
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values {
            get {                
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values {
            get {                
                if (values == null) values = new ValueCollection(this);
                return values;
            }
        }

        public TValue this[TKey key] {
            get {
                var i = FindEntry(key);
                if (i >= 0) return entries[i].value;
                //ThrowHelper.ThrowKeyNotFoundException();
                return default(TValue);
            }
            set {
                Insert(key, value, false);
            }
        }

        public void Add(TKey key, TValue value) {
            Insert(key, value, true);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair) {
            Add(keyValuePair.Key, keyValuePair.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair) {
            var i = FindEntry(keyValuePair.Key);
            if( i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value)) {
                return true;
            }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair) {
            var i = FindEntry(keyValuePair.Key);
            if( i >= 0 && EqualityComparer<TValue>.Default.Equals(entries[i].value, keyValuePair.Value)) {
                Remove(keyValuePair.Key);
                return true;
            }
            return false;
        }

        public void Clear() {
            if (count > 0) {
                for (var i = 0; i < buckets.Length; i++) buckets[i] = -1;
                Array.Clear(entries, 0, count);
                freeList = -1;
                count = 0;
                freeCount = 0;
                version++;
            }
        }

        public bool ContainsKey(TKey key) {
            return FindEntry(key) >= 0;
        }

        public bool ContainsValue(TValue value) {
            if (value == null) {
                for (var i = 0; i < count; i++) {
                    if (entries[i].hashCode >= 0 && entries[i].value == null) return true;
                }
            }
            else {
                var c = EqualityComparer<TValue>.Default;
                for (var i = 0; i < count; i++) {
                    if (entries[i].hashCode >= 0 && c.Equals(entries[i].value, value)) return true;
                }
            }
            return false;
        }

        private void CopyTo(KeyValuePair<TKey,TValue>[] array, int index) {
            //if (array == null) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            //}
            
            //if (index < 0 || index > array.Length ) {
            //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
            //}

            //if (array.Length - index < Count) {
            //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            //}

            var count = this.count;
            var entries = this.entries;
            for (var i = 0; i < count; i++) {
                if (entries[i].hashCode >= 0) {
                    array[index++] = new KeyValuePair<TKey,TValue>(entries[i].key, entries[i].value);
                }
            }
        }

        public Enumerator GetEnumerator() {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }

        private int FindEntry(TKey key) {
            //if( key == null) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            //}

            if (buckets != null) {
                var hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
                for (var i = buckets[hashCode % buckets.Length]; i >= 0; i = entries[i].next) {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key)) return i;
                }
            }
            return -1;
        }

        private void Initialize(int capacity) {
            var size = HashHelpers.GetPrime(capacity);
            buckets = new int[size];
            for (var i = 0; i < buckets.Length; i++) buckets[i] = -1;
            entries = new Entry[size];
            freeList = -1;
        }

        private void Insert(TKey key, TValue value, bool add) {
        
            //if( key == null ) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            //}

            if (buckets == null) Initialize(0);
            var hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
            var targetBucket = hashCode % buckets.Length;

#if FEATURE_RANDOMIZED_STRING_HASHING
            int collisionCount = 0;
#endif

            for (var i = buckets[targetBucket]; i >= 0; i = entries[i].next) {
                if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key)) {
                    //if (add) { 
                    //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_AddingDuplicate);
                    //}
                    entries[i].value = value;
                    version++;
                    return;
                } 

#if FEATURE_RANDOMIZED_STRING_HASHING
                collisionCount++;
#endif
            }
            int index;
            if (freeCount > 0) {
                index = freeList;
                freeList = entries[index].next;
                freeCount--;
            }
            else {
                if (count == entries.Length)
                {
                    Resize();
                    targetBucket = hashCode % buckets.Length;
                }
                index = count;
                count++;
            }

            entries[index].hashCode = hashCode;
            entries[index].next = buckets[targetBucket];
            entries[index].key = key;
            entries[index].value = value;
            buckets[targetBucket] = index;
            version++;

#if FEATURE_RANDOMIZED_STRING_HASHING
            if(collisionCount > HashHelpers.HashCollisionThreshold && HashHelpers.IsWellKnownEqualityComparer(comparer)) 
            {
                comparer = (IEqualityComparer<TKey>) HashHelpers.GetRandomizedEqualityComparer(comparer);
                Resize(entries.Length, true);
            }
#endif

        }

        private void Resize() {
            Resize(HashHelpers.ExpandPrime(count), false);
        }

        private void Resize(int newSize, bool forceNewHashCodes) {
            //Contract.Assert(newSize >= entries.Length);
            var newBuckets = new int[newSize];
            for (var i = 0; i < newBuckets.Length; i++) newBuckets[i] = -1;
            var newEntries = new Entry[newSize];
            Array.Copy(entries, 0, newEntries, 0, count);
            if(forceNewHashCodes) {
                for (var i = 0; i < count; i++) {
                    if(newEntries[i].hashCode != -1) {
                        newEntries[i].hashCode = (comparer.GetHashCode(newEntries[i].key) & 0x7FFFFFFF);
                    }
                }
            }
            for (var i = 0; i < count; i++) {
                if (newEntries[i].hashCode >= 0) {
                    var bucket = newEntries[i].hashCode % newSize;
                    newEntries[i].next = newBuckets[bucket];
                    newBuckets[bucket] = i;
                }
            }
            buckets = newBuckets;
            entries = newEntries;
        }

        public bool Remove(TKey key) {
            //if(key == null) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);
            //}

            if (buckets != null) {
                var hashCode = comparer.GetHashCode(key) & 0x7FFFFFFF;
                var bucket = hashCode % buckets.Length;
                var last = -1;
                for (var i = buckets[bucket]; i >= 0; last = i, i = entries[i].next) {
                    if (entries[i].hashCode == hashCode && comparer.Equals(entries[i].key, key)) {
                        if (last < 0) {
                            buckets[bucket] = entries[i].next;
                        }
                        else {
                            entries[last].next = entries[i].next;
                        }
                        entries[i].hashCode = -1;
                        entries[i].next = freeList;
                        entries[i].key = default(TKey);
                        entries[i].value = default(TValue);
                        freeList = i;
                        freeCount++;
                        version++;
                        return true;
                    }
                }
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value) {
            var i = FindEntry(key);
            if (i >= 0) {
                value = entries[i].value;
                return true;
            }
            value = default(TValue);
            return false;
        }

        // This is a convenience method for the internal callers that were converted from using Hashtable.
        // Many were combining key doesn't exist and key exists but null value (for non-value types) checks.
        // This allows them to continue getting that behavior with minimal code delta. This is basically
        // TryGetValue without the out param
        internal TValue GetValueOrDefault(TKey key) {
            var i = FindEntry(key);
            if (i >= 0) {
                return entries[i].value;
            }
            return default(TValue);
        }

        bool ICollection<KeyValuePair<TKey,TValue>>.IsReadOnly {
            get { return false; }
        }

        void ICollection<KeyValuePair<TKey,TValue>>.CopyTo(KeyValuePair<TKey,TValue>[] array, int index) {
            CopyTo(array, index);
        }

        void ICollection.CopyTo(Array array, int index) {
            //if (array == null) {
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            //}
            
            //if (array.Rank != 1) {
            //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
            //}

            //if( array.GetLowerBound(0) != 0 ) {
            //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
            //}
            
            //if (index < 0 || index > array.Length) {
            //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
            //}

            //if (array.Length - index < Count) {
            //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
            //}
            
            var pairs = array as KeyValuePair<TKey,TValue>[];
            if (pairs != null) {
                CopyTo(pairs, index);
            }
            else if( array is DictionaryEntry[]) {
                var dictEntryArray = array as DictionaryEntry[];
                var entries = this.entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i].hashCode >= 0) {
                        dictEntryArray[index++] = new DictionaryEntry(entries[i].key, entries[i].value);
                    }
                }                
            }
            else {
                var objects = array as object[];
                //if (objects == null) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                //}

                //try {
                    var count = this.count;
                    var entries = this.entries;
                    for (var i = 0; i < count; i++) {
                        if (entries[i].hashCode >= 0) {
                            objects[index++] = new KeyValuePair<TKey,TValue>(entries[i].key, entries[i].value);
                        }
                    }
                //}
                //catch(ArrayTypeMismatchException) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                //}
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return new Enumerator(this, Enumerator.KeyValuePair);
        }
    
        bool ICollection.IsSynchronized {
            get { return false; }
        }

        object ICollection.SyncRoot { 
            get { 
                if( _syncRoot == null) {
                    System.Threading.Interlocked.CompareExchange<Object>(ref _syncRoot, new Object(), null);    
                }
                return _syncRoot; 
            }
        }

        bool IDictionary.IsFixedSize {
            get { return false; }
        }

        bool IDictionary.IsReadOnly {
            get { return false; }
        }

        ICollection IDictionary.Keys {
            get { return (ICollection)Keys; }
        }
    
        ICollection IDictionary.Values {
            get { return (ICollection)Values; }
        }
    
        object IDictionary.this[object key] {
            get { 
                if( IsCompatibleKey(key)) {                
                    var i = FindEntry((TKey)key);
                    if (i >= 0) { 
                        return entries[i].value;                
                    }
                }
                return null;
            }
            set {                 
                //if (key == null)
                //{
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);                          
                //}
                //ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

                //try {
                    var tempKey = (TKey)key;
                    //try {
                        this[tempKey] = (TValue)value; 
                    //}
                    //catch (InvalidCastException) { 
                    //    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));   
                    //}
                //}
                //catch (InvalidCastException) { 
                //    ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
                //}
            }
        }

        private static bool IsCompatibleKey(object key) {
            //if( key == null) {
            //        ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);                          
            //    }
            return (key is TKey); 
        }
    
        void IDictionary.Add(object key, object value) {            
            //if (key == null)
            //{
            //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.key);                          
            //}
            //ThrowHelper.IfNullAndNullsAreIllegalThenThrow<TValue>(value, ExceptionArgument.value);

            //try {
                var tempKey = (TKey)key;

                //try {
                    Add(tempKey, (TValue)value);
                //}
                //catch (InvalidCastException) { 
                //    ThrowHelper.ThrowWrongValueTypeArgumentException(value, typeof(TValue));   
                //}
            //}
            //catch (InvalidCastException) { 
            //    ThrowHelper.ThrowWrongKeyTypeArgumentException(key, typeof(TKey));
            //}
        }
    
        bool IDictionary.Contains(object key) {    
            if(IsCompatibleKey(key)) {
                return ContainsKey((TKey)key);
            }
       
            return false;
        }
    
        IDictionaryEnumerator IDictionary.GetEnumerator() {
            return new Enumerator(this, Enumerator.DictEntry);
        }
    
        void IDictionary.Remove(object key) {            
            if(IsCompatibleKey(key)) {
                Remove((TKey)key);
            }
        }

        public struct Enumerator: IEnumerator<KeyValuePair<TKey,TValue>>,
            IDictionaryEnumerator
        {
            private UnsafeDictionary<TKey,TValue> dictionary;
            private int version;
            private int index;
            private KeyValuePair<TKey,TValue> current;
            private int getEnumeratorRetType;  // What should Enumerator.Current return?
            
            internal const int DictEntry = 1;
            internal const int KeyValuePair = 2;

            internal Enumerator(UnsafeDictionary<TKey,TValue> dictionary, int getEnumeratorRetType) {
                this.dictionary = dictionary;
                version = dictionary.version;
                index = 0;
                this.getEnumeratorRetType = getEnumeratorRetType;
                current = new KeyValuePair<TKey, TValue>();
            }

            public bool MoveNext() {
                //if (version != dictionary.version) {
                //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);
                //}

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
                while ((uint)index < (uint)dictionary.count) {
                    if (dictionary.entries[index].hashCode >= 0) {
                        current = new KeyValuePair<TKey, TValue>(dictionary.entries[index].key, dictionary.entries[index].value);
                        index++;
                        return true;
                    }
                    index++;
                }

                index = dictionary.count + 1;
                current = new KeyValuePair<TKey, TValue>();
                return false;
            }

            public KeyValuePair<TKey,TValue> Current {
                get { return current; }
            }

            public void Dispose() {
            }

            object IEnumerator.Current {
                get { 
                    //if( index == 0 || (index == dictionary.count + 1)) {
                    //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                    //}      

                    if (getEnumeratorRetType == DictEntry) {
                        return new System.Collections.DictionaryEntry(current.Key, current.Value);
                    } else {
                        return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                    }
                }
            }

            void IEnumerator.Reset() {
                //if (version != dictionary.version) {
                //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);
                //}

                index = 0;
                current = new KeyValuePair<TKey, TValue>();    
            }

            DictionaryEntry IDictionaryEnumerator.Entry {
                get { 
                    //if( index == 0 || (index == dictionary.count + 1)) {
                    //     ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                    //}                        
                    
                    return new DictionaryEntry(current.Key, current.Value); 
                }
            }

            object IDictionaryEnumerator.Key {
                get { 
                    //if( index == 0 || (index == dictionary.count + 1)) {
                    //     ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                    //}                        
                    
                    return current.Key; 
                }
            }

            object IDictionaryEnumerator.Value {
                get { 
                    //if( index == 0 || (index == dictionary.count + 1)) {
                    //     ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                    //}                        
                    
                    return current.Value; 
                }
            }
        }

        //[DebuggerTypeProxy(typeof(Mscorlib_DictionaryKeyCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        public sealed class KeyCollection: ICollection<TKey>, ICollection
        {
            private UnsafeDictionary<TKey,TValue> dictionary;

            public KeyCollection(UnsafeDictionary<TKey,TValue> dictionary) {
                //if (dictionary == null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                //}
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator() {
                return new Enumerator(dictionary);
            }

            public void CopyTo(TKey[] array, int index) {
                //if (array == null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                //}

                //if (index < 0 || index > array.Length) {
                //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
                //}

                //if (array.Length - index < dictionary.Count) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                //}
                
                var count = dictionary.count;
                var entries = dictionary.entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i].hashCode >= 0) array[index++] = entries[i].key;
                }
            }

            public int Count {
                get { return dictionary.Count; }
            }

            bool ICollection<TKey>.IsReadOnly {
                get { return true; }
            }

            void ICollection<TKey>.Add(TKey item){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
            }
            
            void ICollection<TKey>.Clear(){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
            }

            bool ICollection<TKey>.Contains(TKey item){
                return dictionary.ContainsKey(item);
            }

            bool ICollection<TKey>.Remove(TKey item){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_KeyCollectionSet);
                return false;
            }
            
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return new Enumerator(dictionary);                
            }

            void ICollection.CopyTo(Array array, int index) {
                //if (array==null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                //}

                //if (array.Rank != 1) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                //}

                //if( array.GetLowerBound(0) != 0 ) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                //}

                //if (index < 0 || index > array.Length) {
                //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
                //}

                //if (array.Length - index < dictionary.Count) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                //}
                
                var keys = array as TKey[];
                if (keys != null) {
                    CopyTo(keys, index);
                }
                else {
                    var objects = array as object[];
                    //if (objects == null) {
                    //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                    //}
                                         
                    var count = dictionary.count;
                    var entries = dictionary.entries;
                    //try {
                        for (var i = 0; i < count; i++) {
                            if (entries[i].hashCode >= 0) objects[index++] = entries[i].key;
                        }
                    //}                    
                    //catch(ArrayTypeMismatchException) {
                    //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                    //}
                }
            }

            bool ICollection.IsSynchronized {
                get { return false; }
            }

            Object ICollection.SyncRoot { 
                get { return ((ICollection)dictionary).SyncRoot; }
            }

            public struct Enumerator : IEnumerator<TKey>, System.Collections.IEnumerator
            {
                private UnsafeDictionary<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TKey currentKey;
            
                internal Enumerator(UnsafeDictionary<TKey, TValue> dictionary) {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = 0;
                    currentKey = default(TKey);                    
                }

                public void Dispose() {
                }

                public bool MoveNext() {
                    //if (version != dictionary.version) {
                    //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);
                    //}

                    while ((uint)index < (uint)dictionary.count) {
                        if (dictionary.entries[index].hashCode >= 0) {
                            currentKey = dictionary.entries[index].key;
                            index++;
                            return true;
                        }
                        index++;
                    }

                    index = dictionary.count + 1;
                    currentKey = default(TKey);
                    return false;
                }
                
                public TKey Current {
                    get {                        
                        return currentKey;
                    }
                }

                Object System.Collections.IEnumerator.Current {
                    get {                      
                        //if( index == 0 || (index == dictionary.count + 1)) {
                        //     ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                        //}                        
                        
                        return currentKey;
                    }
                }
                
                void System.Collections.IEnumerator.Reset() {
                    //if (version != dictionary.version) {
                    //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);                        
                    //}

                    index = 0;                    
                    currentKey = default(TKey);
                }
            }                        
        }

        //[DebuggerTypeProxy(typeof(Mscorlib_DictionaryValueCollectionDebugView<,>))]
        [DebuggerDisplay("Count = {Count}")]
        public sealed class ValueCollection: ICollection<TValue>, ICollection
        {
            private UnsafeDictionary<TKey,TValue> dictionary;

            public ValueCollection(UnsafeDictionary<TKey,TValue> dictionary) {
                //if (dictionary == null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.dictionary);
                //}
                this.dictionary = dictionary;
            }

            public Enumerator GetEnumerator() {
                return new Enumerator(dictionary);                
            }

            public void CopyTo(TValue[] array, int index) {
                //if (array == null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                //}

                //if (index < 0 || index > array.Length) {
                //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
                //}

                //if (array.Length - index < dictionary.Count) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                //}
                
                var count = dictionary.count;
                var entries = dictionary.entries;
                for (var i = 0; i < count; i++) {
                    if (entries[i].hashCode >= 0) array[index++] = entries[i].value;
                }
            }

            public int Count {
                get { return dictionary.Count; }
            }

            bool ICollection<TValue>.IsReadOnly {
                get { return true; }
            }

            void ICollection<TValue>.Add(TValue item){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
            }

            bool ICollection<TValue>.Remove(TValue item){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
                return false;
            }

            void ICollection<TValue>.Clear(){
                //ThrowHelper.ThrowNotSupportedException(ExceptionResource.NotSupported_ValueCollectionSet);
            }

            bool ICollection<TValue>.Contains(TValue item){
                return dictionary.ContainsValue(item);
            }

            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() {
                return new Enumerator(dictionary);
            }

            IEnumerator IEnumerable.GetEnumerator() {
                return new Enumerator(dictionary);                
            }

            void ICollection.CopyTo(Array array, int index) {
                //if (array == null) {
                //    ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
                //}

                //if (array.Rank != 1) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_RankMultiDimNotSupported);
                //}

                //if( array.GetLowerBound(0) != 0 ) {
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_NonZeroLowerBound);
                //}

                //if (index < 0 || index > array.Length) { 
                //    ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index, ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
                //}

                //if (array.Length - index < dictionary.Count)
                //    ThrowHelper.ThrowArgumentException(ExceptionResource.Arg_ArrayPlusOffTooSmall);
                
                var values = array as TValue[];
                if (values != null) {
                    CopyTo(values, index);
                }
                else {
                    var objects = array as object[];
                    //if (objects == null) {
                    //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                    //}

                    var count = dictionary.count;
                    var entries = dictionary.entries;
                    //try {
                        for (var i = 0; i < count; i++) {
                            if (entries[i].hashCode >= 0) objects[index++] = entries[i].value;
                        }
                    //}
                    //catch(ArrayTypeMismatchException) {
                    //    ThrowHelper.ThrowArgumentException(ExceptionResource.Argument_InvalidArrayType);
                    //}
                }
            }

            bool ICollection.IsSynchronized {
                get { return false; }
            }

            Object ICollection.SyncRoot { 
                get { return ((ICollection)dictionary).SyncRoot; }
            }

            public struct Enumerator : IEnumerator<TValue>, System.Collections.IEnumerator
            {
                private UnsafeDictionary<TKey, TValue> dictionary;
                private int index;
                private int version;
                private TValue currentValue;
            
                internal Enumerator(UnsafeDictionary<TKey, TValue> dictionary) {
                    this.dictionary = dictionary;
                    version = dictionary.version;
                    index = 0;
                    currentValue = default(TValue);
                }

                public void Dispose() {
                }

                public bool MoveNext() {                    
                    //if (version != dictionary.version) {
                    //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);
                    //}
                    
                    while ((uint)index < (uint)dictionary.count) {
                        if (dictionary.entries[index].hashCode >= 0) {
                            currentValue = dictionary.entries[index].value;
                            index++;
                            return true;
                        }
                        index++;
                    }
                    index = dictionary.count + 1;
                    currentValue = default(TValue);
                    return false;
                }
                
                public TValue Current {
                    get {                        
                        return currentValue;
                    }
                }

                Object System.Collections.IEnumerator.Current {
                    get {                      
                        //if( index == 0 || (index == dictionary.count + 1)) {
                        //     ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumOpCantHappen);                        
                        //}                        
                        
                        return currentValue;
                    }
                }
                
                void System.Collections.IEnumerator.Reset() {
                    //if (version != dictionary.version) {
                    //    ThrowHelper.ThrowInvalidOperationException(ExceptionResource.InvalidOperation_EnumFailedVersion);
                    //}
                    index = 0;                    
                    currentValue = default(TValue);
                }
            }
        }
    }
}
