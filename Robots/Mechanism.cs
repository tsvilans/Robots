﻿using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Xml.Linq;
using System.Xml;
using static Robots.Util;
using static System.Math;

namespace Robots
{
    public abstract class Mechanism
    {
        readonly string model;
        public Manufacturers Manufacturer { get; protected set; }
        public string Model => $"{Manufacturer.ToString()}.{model}";
        public double Payload { get; }
        internal Plane BasePlane { get; set; }
        internal Mesh BaseMesh { get; }
        public Joint[] Joints { get; }
        public bool MovesRobot { get; }
        public Mesh DisplayMesh { get; private set; }

        internal Mechanism(string model, Manufacturers manufacturer, double payload, Plane basePlane, Mesh baseMesh, IEnumerable<Joint> joints, bool movesRobot)
        {
            this.model = model;
            this.Manufacturer = manufacturer;
            this.Payload = payload;
            this.BasePlane = basePlane;
            this.BaseMesh = baseMesh;
            this.Joints = joints.ToArray();
            this.MovesRobot = movesRobot;

            this.DisplayMesh = CreateDisplayMesh();

            // Joints to radians
            for (int i = 0; i < Joints.Length; i++)
            {
                Joints[i].Range = new Interval(DegreeToRadian(Joints[i].Range.T0, i), DegreeToRadian(Joints[i].Range.T1, i));
            }

            SetStartPlanes();
        }

        Mesh CreateDisplayMesh()
        {
            var mesh = new Mesh();
            mesh.Append(BaseMesh);

            foreach (var joint in Joints)
                mesh.Append(joint.Mesh);

            mesh.Transform(BasePlane.ToTransform());
            return mesh;
        }

        static List<Mesh> GetMeshes(string model)
        {
            var meshes = new List<Mesh>();

            /*
            using (var stream = new MemoryStream(Properties.Resources.Meshes))
            {
                var formatter = new BinaryFormatter();
                JointMeshes jointMeshes = formatter.Deserialize(stream) as JointMeshes;
                index = jointMeshes.Names.FindIndex(x => x == model);
                if (index != -1) meshes = jointMeshes.Meshes[index];
            }
            */

            // string folder = $@"{AssemblyDirectory}\robots";
            string folder = LibraryPath;

            if (Directory.Exists(folder))
            {
                var files = Directory.GetFiles(folder, "*.3dm");
                Rhino.DocObjects.Layer layer = null;

                foreach (var file in files)
                {
                    Rhino.FileIO.File3dm geometry = Rhino.FileIO.File3dm.Read($@"{file}");
                    layer = geometry.Layers.FirstOrDefault(x => x.Name == $"{model}");

                    if (layer != null)
                    {
                        int i = 0;
                        while (true)
                        {
                            string name = $"{i++}";
                            var jointLayer = geometry.Layers.FirstOrDefault(x => (x.Name == name) && (x.ParentLayerId == layer.Id));
                            if (jointLayer == null) break;
                            meshes.Add(geometry.Objects.First(x => x.Attributes.LayerIndex == jointLayer.LayerIndex).Geometry as Mesh);
                        }
                        break;
                    }
                }
                if (layer == null)
                    throw new InvalidOperationException($" Robot \"{model}\" is not in the geometry file.");
            }

            return meshes;
        }

        internal static Mechanism Create(XElement element)
        {
            var modelName = element.Attribute(XName.Get("model")).Value;
            var manufacturer = (Manufacturers)Enum.Parse(typeof(Manufacturers), element.Attribute(XName.Get("manufacturer")).Value);
            string fullName = $"{element.Name.LocalName}.{manufacturer.ToString()}.{modelName}";

            bool movesRobot = false;
            var movesRobotAttribute = element.Attribute(XName.Get("movesRobot"));
            if (movesRobotAttribute != null) movesRobot = XmlConvert.ToBoolean(movesRobotAttribute.Value);

            var meshes = GetMeshes(fullName);

            double payload = Convert.ToDouble(element.Attribute(XName.Get("payload")).Value);

            var baseMesh = meshes[0].DuplicateMesh();
            XElement baseElement = element.Element(XName.Get("Base"));
            double x = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("x")).Value);
            double y = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("y")).Value);
            double z = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("z")).Value);
            double q1 = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("q1")).Value);
            double q2 = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("q2")).Value);
            double q3 = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("q3")).Value);
            double q4 = XmlConvert.ToDouble(baseElement.Attribute(XName.Get("q4")).Value);
            var basePlane = Util.QuaternionToPlane(x, y, z, q1, q2, q3, q4);

            var jointElements = element.Element(XName.Get("Joints")).Descendants().ToArray();
            Joint[] joints = new Joint[jointElements.Length];

            for (int i = 0; i < jointElements.Length; i++)
            {
                var jointElement = jointElements[i];
                double a = XmlConvert.ToDouble(jointElement.Attribute(XName.Get("a")).Value);
                double d = XmlConvert.ToDouble(jointElement.Attribute(XName.Get("d")).Value);
                string text = jointElement.Attribute(XName.Get("minrange")).Value;
                double minRange = XmlConvert.ToDouble(text);
                double maxRange = XmlConvert.ToDouble(jointElement.Attribute(XName.Get("maxrange")).Value);
                Interval range = new Interval(minRange, maxRange);
                double maxSpeed = XmlConvert.ToDouble(jointElement.Attribute(XName.Get("maxspeed")).Value);
                Mesh mesh = meshes[i + 1].DuplicateMesh();
                int number = XmlConvert.ToInt32(jointElement.Attribute(XName.Get("number")).Value) - 1;

                if (jointElement.Name == "Revolute")
                    joints[i] = new RevoluteJoint() { Index = i, Number = number, A = a, D = d, Range = range, MaxSpeed = maxSpeed.ToRadians(), Mesh = mesh };
                else if (jointElement.Name == "Prismatic")
                    joints[i] = new PrismaticJoint() { Index = i, Number = number, A = a, D = d, Range = range, MaxSpeed = maxSpeed, Mesh = mesh };
            }

            switch (element.Name.ToString())
            {
                case ("RobotArm"):
                    {
                        switch (manufacturer)
                        {
                            case (Manufacturers.ABB):
                                return new RobotAbb(modelName, payload, basePlane, baseMesh, joints);
                            case (Manufacturers.KUKA):
                                return new RobotKuka(modelName, payload, basePlane, baseMesh, joints);
                            case (Manufacturers.UR):
                                return new RobotUR(modelName, payload, basePlane, baseMesh, joints);
                            default:
                                return null;
                        }
                    }
                case ("Positioner"):
                    return new Positioner(modelName, manufacturer, payload, basePlane, baseMesh, joints, movesRobot);
                case ("Track"):
                    return new Track(modelName, manufacturer, payload, basePlane, baseMesh, joints, movesRobot);
                default:
                    return null;
            }

        }

        /*
        public static void WriteMeshes()
        {
            Rhino.FileIO.File3dm robotsGeometry = Rhino.FileIO.File3dm.Read($@"{ResourcesFolder}\robotsGeometry.3dm");
            var jointmeshes = new JointMeshes();

            foreach (var layer in robotsGeometry.Layers)
            {
                if (layer.Name == "Default" || layer.ParentLayerId != Guid.Empty) continue;
                jointmeshes.Names.Add(layer.Name);
                var meshes = new List<Mesh>();
                meshes.Add(robotsGeometry.Objects.First(x => x.Attributes.LayerIndex == layer.LayerIndex).Geometry as Mesh);

                int i = 0;
                while (true)
                {
                    string name = $"{i++ + 1}";
                    var jointLayer = robotsGeometry.Layers.FirstOrDefault(x => (x.Name == name) && (x.ParentLayerId == layer.Id));
                    if (jointLayer == null) break;
                    meshes.Add(robotsGeometry.Objects.First(x => x.Attributes.LayerIndex == jointLayer.LayerIndex).Geometry as Mesh);
                }
                jointmeshes.Meshes.Add(meshes);
            }

            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, jointmeshes);
                File.WriteAllBytes($@"{ResourcesFolder}\Meshes.rob", stream.ToArray());
            }
        }
        */

        public abstract KinematicSolution Kinematics(Target target, double[] prevJoints = null, bool displayMeshes = false, Plane? basePlane = null);

        protected abstract void SetStartPlanes();
        public abstract double DegreeToRadian(double degree, int i);
        public abstract double RadianToDegree(double radian, int i);
        public override string ToString() => $"{this.GetType().Name} ({Model})";

        protected abstract class MechanismKinematics : KinematicSolution
        {
            protected Mechanism mechanism;

            internal MechanismKinematics(Mechanism mechanism, Target target, double[] prevJoints, bool displayMeshes, Plane? basePlane)
            {
                this.mechanism = mechanism;
                int jointCount = mechanism.Joints.Length;

                // Init properties
                Joints = new double[jointCount];
                Planes = new Plane[jointCount + 1];
                if (displayMeshes)
                    Meshes = new Mesh[jointCount + 1];
                else
                    Meshes = new Mesh[0];

                // Base plane
                Planes[0] = mechanism.BasePlane;

                if (basePlane != null)
                {
                    Planes[0].Transform(Transform.PlaneToPlane(Plane.WorldXY,(Plane)basePlane));
                }

                SetJoints(target, prevJoints);
                JointsOutOfRange();

                SetPlanes(target);

                // Move planes to base
                var transform = Planes[0].ToTransform();
                for (int i = 1; i < jointCount + 1; i++)
                    Planes[i].Transform(transform);

                // Meshes
                if (displayMeshes)
                    SetMeshes(target.Tool);
            }

            protected abstract void SetJoints(Target target, double[] prevJoints);
            protected abstract void SetPlanes(Target target);

            protected virtual void JointsOutOfRange()
            {
                var outofRangeErrors = mechanism.Joints
                .Where(x => !x.Range.IncludesParameter(Joints[x.Index]))
                .Select(x => $"Axis {x.Number + 1} is outside the permited range.");
                Errors.AddRange(outofRangeErrors);
            }

            void SetMeshes(Tool tool)
            {
                {
                    Meshes[0] = mechanism.BaseMesh.DuplicateMesh();
                    Meshes[0].Transform(Planes[0].ToTransform());
                }

                for (int i = 0; i < mechanism.Joints.Length; i++)
                {
                    var jointMesh = mechanism.Joints[i].Mesh.DuplicateMesh();
                    jointMesh.Transform(Transform.PlaneToPlane(mechanism.Joints[i].Plane, Planes[i + 1]));
                    Meshes[i + 1] = jointMesh;
                }
            }
        }
    }

    public abstract class Joint
    {
        public int Index { get; set; }
        public int Number { get; set; }
        internal double A { get; set; }
        internal double D { get; set; }
        public Interval Range { get; internal set; }
        public double MaxSpeed { get; internal set; }
        internal Plane Plane { get; set; }
        internal Mesh Mesh { get; set; }
    }

    public class BaseJoint
    {

    }

    public class RevoluteJoint : Joint
    {

    }

    public class PrismaticJoint : Joint
    {
    }


    [Serializable]
    class JointMeshes
    {
        internal List<string> Names { get; set; } = new List<string>();
        internal List<List<Mesh>> Meshes { get; set; } = new List<List<Mesh>>();
    }
}