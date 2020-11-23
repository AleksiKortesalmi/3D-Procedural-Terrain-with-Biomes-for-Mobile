using System;
using UnityEngine;
using System.Collections.Generic;

namespace Utility
{
    [Serializable]
    public class Vector3Range
    {
        [SerializeField]
        private Vector3 min;
        public Vector3 Min { get => min; }
        [SerializeField]
        private Vector3 max;
        public Vector3 Max { get => max; }

        public Vector3Range(Vector3 min, Vector3 max)
        {
            this.min = min;
            this.max = max;
        }

        public Vector3 RandomVector3()
        {
            return new Vector3(UnityEngine.Random.Range(min.x, max.x), UnityEngine.Random.Range(min.y, max.y), UnityEngine.Random.Range(min.z, max.z));
        }
    }

    public static class InsertionSorting
    {
        public static void SortByDistance3D<T>(this List<T> list, Func<T, Vector3> getPosition, Vector3 target)
        {
            int n = list.Count, i, j, flag;
            float valDistance;
            T val;

            for (i = 1; i < n; i++)
            {
                valDistance = (getPosition(list[i]) - target).sqrMagnitude;
                val = list[i];
                flag = 0;
                for (j = i - 1; j >= 0 && flag != 1;)
                {
                    if (valDistance < (getPosition(list[j]) - target).sqrMagnitude)
                    {
                        list[j + 1] = list[j];
                        j--;
                        list[j + 1] = val;
                    }
                    else flag = 1;
                }
            }
        }

        public static void SortByDistance2D<T>(this List<T> list, Func<T, Vector2> getPosition, Vector2 target)
        {
            int n = list.Count, i, j, flag;
            float valDistance;
            T val;

            for (i = 1; i < n; i++)
            {
                valDistance = (getPosition(list[i]) - target).sqrMagnitude;
                val = list[i];
                flag = 0;
                for (j = i - 1; j >= 0 && flag != 1;)
                {
                    if (valDistance < (getPosition(list[j]) - target).sqrMagnitude)
                    {
                        list[j + 1] = list[j];
                        j--;
                        list[j + 1] = val;
                    }
                    else flag = 1;
                }
            }
        }
    }
}