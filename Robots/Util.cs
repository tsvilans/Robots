﻿using System;
using static System.Math;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rhino.Geometry;
using System.Reflection;
using System.IO;

namespace Robots
{
    internal static class Util
    {
        public const double DistanceTol = 0.001;
        public const double AngleTol = 0.001;
        public const double TimeTol = 0.00001;
        public const double UnitTol = 0.000001;
        internal const double SingularityTol = 0.0001;

        // internal const string ResourcesFolder = @"C:\Users\vicen\Documents\Work\Bartlett\RobotsApp\Robots\Robots\Resources";

        internal static Transform ToTransform(this double[,] matrix)
        {
            var transform = new Transform();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    transform[i, j] = matrix[i, j];

            return transform;
        }

        internal static Plane ToPlane(this Transform transform)
        {
            Plane plane = Plane.WorldXY;
            plane.Transform(transform);
            return plane;
        }

        internal static Transform ToTransform(this Plane plane)
        {
            return Transform.PlaneToPlane(Plane.WorldXY, plane);
        }

        internal static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        internal static string LibraryPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Robots");
            }
        }

        internal static double ToRadians(this double value)
        {
            return value * (PI / 180);
        }

        internal static double ToDegrees(this double value)
        {
            return value * (180 / PI);
        }

        public static T[] Subset<T>(this T[] array, int[] indices)
        {
            T[] subset = new T[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                subset[i] = array[indices[i]];
            }
            return subset;
        }

        public static T[] RangeSubset<T>(this T[] array, int startIndex, int length)
        {
            T[] subset = new T[length];
            Array.Copy(array, startIndex, subset, 0, length);
            return subset;
        }

        public static IEnumerable<List<T>> Transpose<T>(this IEnumerable<IEnumerable<T>> source)
        {
            var enumerators = source.Select(e => e.GetEnumerator()).ToArray();
            try
            {
                while (enumerators.All(e => e.MoveNext()))
                {
                    yield return enumerators.Select(e => e.Current).ToList();
                }
            }
            finally
            {
                foreach (var enumerator in enumerators)
                    enumerator.Dispose();
            }
        }

        #region ABB quaternion conversions
        internal static Plane QuaternionToPlane(Point3d point, Quaternion quaternion)
        {
            quaternion.GetRotation(out Plane plane);
            plane.Origin = point;
            return plane;
        }

        internal static Plane QuaternionToPlane(double x, double y, double z, double q1, double q2, double q3, double q4)
        {
            var point = new Point3d(x, y, z);
            var quaternion = new Quaternion(q1, q2, q3, q4);
            return QuaternionToPlane(point, quaternion);
        }

        internal static double[] PlaneToQuaternion(Plane plane)
        {
            var q = Quaternion.Rotation(Plane.WorldXY, plane);
            return new double[] { plane.OriginX, plane.OriginY, plane.OriginZ, q.A, q.B, q.C, q.D };
        }
        #endregion
    }
}