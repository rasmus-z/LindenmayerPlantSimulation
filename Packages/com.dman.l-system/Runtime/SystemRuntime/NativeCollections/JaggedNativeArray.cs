﻿using System;
using System.Linq;
using System.Runtime.Serialization;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Dman.LSystem.SystemRuntime.NativeCollections
{
    [Serializable]
    public struct JaggedNativeArray<TData> :
        System.IEquatable<JaggedNativeArray<TData>>,
        IDisposable,
        INativeDisposable,
        ISerializable
        where TData : unmanaged
    {
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction] // disable all safety to allow parallel writes
        public NativeArray<TData> data;
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction] // disable all safety to allow parallel writes
        public NativeArray<JaggedIndexing> indexing;

        public int Length => indexing.Length;
        public bool IsCreated => data.IsCreated && indexing.IsCreated;

        public JaggedIndexing this[int index]
        {
            get => indexing[index];
            set => indexing[index] = value;
        }
        public TData this[int index, int indexInJagged]
        {
            get
            {
                var jagged = indexing[index];
                var realIndex = jagged.index + indexInJagged;
                return data[realIndex];
            }
            set
            {
                var jagged = indexing[index];
                var realIndex = jagged.index + indexInJagged;
                data[realIndex] = value;
            }
        }
        public TData this[JaggedIndexing jagged, int indexInJagged]
        {
            get
            {
                var realIndex = jagged.index + indexInJagged;
                return data[realIndex];
            }
            set
            {
                var realIndex = jagged.index + indexInJagged;
                data[realIndex] = value;
            }
        }

        private Allocator allocatorUsed;

        public JaggedNativeArray(JaggedNativeArray<TData> jaggedData, Allocator allocator)
        {
            allocatorUsed = allocator;
            indexing = new NativeArray<JaggedIndexing>(jaggedData.indexing, allocator);
            data = new NativeArray<TData>(jaggedData.data, allocator);
        }

        public JaggedNativeArray(int firstDimensionSize, int totalDataSize, Allocator allocator, NativeArrayOptions initializationOptions = NativeArrayOptions.UninitializedMemory)
        {
            allocatorUsed = allocator;
            indexing = new NativeArray<JaggedIndexing>(firstDimensionSize, allocator, initializationOptions);
            data = new NativeArray<TData>(totalDataSize, allocator, initializationOptions);
        }

        public JaggedNativeArray(TData[][] jaggedData, Allocator allocator)
        {
            allocatorUsed = allocator;
            indexing = new NativeArray<JaggedIndexing>(jaggedData.Length, allocator, NativeArrayOptions.UninitializedMemory);

            var paramSum = jaggedData.Select(x => x.Length).Sum();
            data = new NativeArray<TData>(paramSum, allocator, NativeArrayOptions.UninitializedMemory);


            var localIndexingArrayHandle = indexing;
            WriteJaggedIndexing(
                (index, indexData) =>
                {
                    localIndexingArrayHandle[index] = indexData;
                },
                jaggedData,
                data
                );
        }

        public static void WriteJaggedIndexing(
            Action<int, JaggedIndexing> writeIndexing,
            TData[][] jaggedData,
            NativeArray<TData> dataArray,
            int originInDataArray = 0)
        {
            var myDataSum = jaggedData.Select(x => x.Length).Sum();
            if (dataArray.Length + originInDataArray < myDataSum)
            {
                throw new Exception("data array not big enough");
            }

            myDataSum = 0;
            for (int i = 0; i < jaggedData.Length; i++)
            {
                var indexInData = myDataSum + originInDataArray;
                var newIndexing = new JaggedIndexing
                {
                    index = indexInData,
                    length = (ushort)jaggedData[i].Length
                };
                writeIndexing(i, newIndexing);
                for (int j = 0; j < jaggedData[i].Length; j++)
                {
                    dataArray[indexInData + j] = jaggedData[i][j];
                }
                myDataSum += jaggedData[i].Length;
            }

        }

        public void CopyFrom(JaggedNativeArray<TData> source, int targetIndex, int targetParamIndex)
        {
            for (int i = 0; i < source.Length; i++)
            {
                var replacementParamIndexing = source.indexing[i];

                indexing[targetIndex + i] = new JaggedIndexing
                {
                    index = targetParamIndex + replacementParamIndexing.index,
                    length = replacementParamIndexing.length
                };
            }
            for (int i = 0; i < source.data.Length; i++)
            {
                data[targetParamIndex + i] = source.data[i];
            }
        }

        public TData[] AsArray(int index)
        {
            var indexes = indexing[index];
            var result = new TData[indexes.length];
            for (int i = 0; i < indexes.length; i++)
            {
                result[i] = data[i + indexes.index];
            }
            return result;
        }

        public bool Equals(JaggedNativeArray<TData> other)
        {
            if(other.Length != this.Length)
            {
                return false;
            }
            for (int i = 0; i < indexing.Length; i++)
            {
                if (!other.indexing[i].Equals(indexing[i]))
                {
                    return false;
                }
            }
            for (int i = 0; i < data.Length; i++)
            {
                if (!other.data[i].Equals(data[i]))
                {
                    return false;
                }
            }
            return true;
        }
        public void Dispose()
        {
            data.Dispose();
            indexing.Dispose();
        }

        public JobHandle Dispose(JobHandle inputDeps)
        {
            return JobHandle.CombineDependencies(
                data.Dispose(inputDeps),
                indexing.Dispose(inputDeps));
        }

        #region Serialization

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("allocator", allocatorUsed);
            info.AddValue("data", data.ToArray()); // TODO: can these serialize directly?
            info.AddValue("indexing", indexing.ToArray());
        }

        // The special constructor is used to deserialize values.
        private JaggedNativeArray(SerializationInfo info, StreamingContext context)
        {
            allocatorUsed = info.GetValue<Allocator>("allocator");

            var dataArray = info.GetValue<TData[]>("data");
            data = new NativeArray<TData>(dataArray, allocatorUsed);

            var indexingArray = info.GetValue<JaggedIndexing[]>("indexing");
            indexing = new NativeArray<JaggedIndexing>(indexingArray, allocatorUsed);
        }
        #endregion
    }

    [Serializable]
    public struct JaggedIndexing : IEquatable<JaggedIndexing>
    {
        /// <summary>
        /// -1 is used for invalid/not populated
        /// </summary>
        public int index;
        public ushort length;
        public int Start => index;
        public int End => index + length;

        public static JaggedIndexing INVALID = new JaggedIndexing
        {
            index = -1,
            length = 0,
        };

        public static JaggedIndexing GetWithNoLength(int index)
        {
            return new JaggedIndexing
            {
                index = index,
                length = 0
            };
        }
        public static JaggedIndexing GetWithOnlyLength(ushort length)
        {
            return new JaggedIndexing
            {
                index = -1,
                length = length
            };
        }

        public T GetValue<T>(NativeArray<T> array, ushort indexInSelf) where T : unmanaged
        {
            return array[indexInSelf + index];
        }

        public bool ContainsIndex(int index)
        {
            return index >= this.Start && index < this.End;
        }

        public bool Equals(JaggedIndexing other)
        {
            return other.index == index && other.length == length;
        }
        public override bool Equals(object obj)
        {
            if (obj is JaggedIndexing indexing)
            {
                return Equals(indexing);
            }
            return false;
        }
        public override int GetHashCode()
        {
            return index << 31 | length;
        }
    }

}
