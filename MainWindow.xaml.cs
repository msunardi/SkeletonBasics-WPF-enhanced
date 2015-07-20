//------------------------------------------------------------------------------
// <copyright file="MainWindow.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace Microsoft.Samples.Kinect.SkeletonBasics
{
    using System.IO;
    using System.Windows;
    using System.Windows.Media;
    using Microsoft.Kinect;
    using System.Globalization;
    using System;
    using System.Diagnostics;

    public static class FloatExt
    {
        public static float Truncate(this float value, int digits)
        {
            double mult = Math.Pow(10.0, digits);
            double result = Math.Truncate(mult * value) / mult;
            return (float)result;
        }
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Width of output drawing
        /// </summary>
        private const float RenderWidth = 640.0f;

        /// <summary>
        /// Height of our output drawing
        /// </summary>
        private const float RenderHeight = 480.0f;

        /// <summary>
        /// Thickness of drawn joint lines
        /// </summary>
        private const double JointThickness = 3;

        /// <summary>
        /// Thickness of body center ellipse
        /// </summary>
        private const double BodyCenterThickness = 10;

        /// <summary>
        /// Thickness of clip edge rectangles
        /// </summary>
        private const double ClipBoundsThickness = 10;

        /// <summary>
        /// Brush used to draw skeleton center point
        /// </summary>
        private readonly Brush centerPointBrush = Brushes.Blue;

        /// <summary>
        /// Brush used for drawing joints that are currently tracked
        /// </summary>
        private readonly Brush trackedJointBrush = new SolidColorBrush(Color.FromArgb(255, 68, 192, 68));

        /// <summary>
        /// Brush used for drawing joints that are currently inferred
        /// </summary>        
        private readonly Brush inferredJointBrush = Brushes.Yellow;

        /// <summary>
        /// Pen used for drawing bones that are currently tracked
        /// </summary>
        private readonly Pen trackedBonePen = new Pen(Brushes.Green, 6);

        /// <summary>
        /// Pen used for drawing bones that are currently inferred
        /// </summary>        
        private readonly Pen inferredBonePen = new Pen(Brushes.Gray, 1);

        /// <summary>
        /// Active Kinect sensor
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Drawing group for skeleton rendering output
        /// </summary>
        private DrawingGroup drawingGroup;

        /// <summary>
        /// Drawing image that we will display
        /// </summary>
        private DrawingImage imageSource;

        private Boolean tableheaderfirst = true;
        private String filepath = @"D:\Kinect_clips\071415_clips\arm_gesture_emphasis3.csv";
        private Stopwatch stopWatch = new Stopwatch();
        private Boolean skeletonReady = false;
        /// <summary>
        /// Initializes a new instance of the MainWindow class.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Draws indicators to show which edges are clipping skeleton data
        /// </summary>
        /// <param name="skeleton">skeleton to draw clipping information for</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private static void RenderClippedEdges(Skeleton skeleton, DrawingContext drawingContext)
        {
            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Bottom))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, RenderHeight - ClipBoundsThickness, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Top))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, RenderWidth, ClipBoundsThickness));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Left))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(0, 0, ClipBoundsThickness, RenderHeight));
            }

            if (skeleton.ClippedEdges.HasFlag(FrameEdges.Right))
            {
                drawingContext.DrawRectangle(
                    Brushes.Red,
                    null,
                    new Rect(RenderWidth - ClipBoundsThickness, 0, ClipBoundsThickness, RenderHeight));
            }
        }

        /// <summary>
        /// Execute startup tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Create the drawing group we'll use for drawing
            this.drawingGroup = new DrawingGroup();

            // Create an image source that we can use in our image control
            this.imageSource = new DrawingImage(this.drawingGroup);

            // Display the drawing using our image control
            Image.Source = this.imageSource;

            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit (See components in Toolkit Browser).
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {
                // Turn on the skeleton stream to receive skeleton frames
                this.sensor.SkeletonStream.Enable();

                // Add an event handler to be called whenever there is new color frame data
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                using (System.IO.StreamWriter file = new System.IO.StreamWriter(filepath, true))
                {
                    file.WriteLine("Start skeleton detection\nJoint name,(x),(y),(z)\n");
                }

                // Start the sensor!
                try
                {
                    this.sensor.Start();                    
                }
                catch (IOException)
                {
                    this.sensor = null;
                }
                
            }

            if (null == this.sensor)
            {
                this.statusBarText.Text = Properties.Resources.NoKinectReady;
            }
        }

        /// <summary>
        /// Execute shutdown tasks
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }

        /// <summary>
        /// Event handler for Kinect sensor's SkeletonFrameReady event
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
            }

            using (DrawingContext dc = this.drawingGroup.Open())
            {
                // Draw a transparent background to set the render size
                dc.DrawRectangle(Brushes.Black, null, new Rect(0.0, 0.0, RenderWidth, RenderHeight));

                if (skeletons.Length != 0)
                {
                    if (!skeletonReady)
                    {
                        stopWatch.Start();
                        skeletonReady = true;
                    }
                    foreach (Skeleton skel in skeletons)
                    {
                        RenderClippedEdges(skel, dc);

                        if (skel.TrackingState == SkeletonTrackingState.Tracked)
                        {
                            this.DrawBonesAndJoints(skel, dc);
                        }
                        else if (skel.TrackingState == SkeletonTrackingState.PositionOnly)
                        {
                            dc.DrawEllipse(
                            this.centerPointBrush,
                            null,
                            this.SkeletonPointToScreen(skel.Position),
                            BodyCenterThickness,
                            BodyCenterThickness);
                        }
                    }
                }                

                // prevent drawing outside of our render area
                this.drawingGroup.ClipGeometry = new RectangleGeometry(new Rect(0.0, 0.0, RenderWidth, RenderHeight));
            }
        }

        /// <summary>
        /// Draws a skeleton's bones and joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        private void DrawBonesAndJoints(Skeleton skeleton, DrawingContext drawingContext)
        {
            // Render Torso
            this.DrawBone(skeleton, drawingContext, JointType.Head, JointType.ShoulderCenter);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.ShoulderRight);
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderCenter, JointType.Spine);
            this.DrawBone(skeleton, drawingContext, JointType.Spine, JointType.HipCenter);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipLeft);
            this.DrawBone(skeleton, drawingContext, JointType.HipCenter, JointType.HipRight);

            // Left Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderLeft, JointType.ElbowLeft);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowLeft, JointType.WristLeft);
            this.DrawBone(skeleton, drawingContext, JointType.WristLeft, JointType.HandLeft);

            // Right Arm
            this.DrawBone(skeleton, drawingContext, JointType.ShoulderRight, JointType.ElbowRight);
            this.DrawBone(skeleton, drawingContext, JointType.ElbowRight, JointType.WristRight);
            this.DrawBone(skeleton, drawingContext, JointType.WristRight, JointType.HandRight);

            // Left Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipLeft, JointType.KneeLeft);
            this.DrawBone(skeleton, drawingContext, JointType.KneeLeft, JointType.AnkleLeft);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleLeft, JointType.FootLeft);

            // Right Leg
            this.DrawBone(skeleton, drawingContext, JointType.HipRight, JointType.KneeRight);
            this.DrawBone(skeleton, drawingContext, JointType.KneeRight, JointType.AnkleRight);
            this.DrawBone(skeleton, drawingContext, JointType.AnkleRight, JointType.FootRight);

            float joint_x = 10;
            float joint_y = 10;
            string pox = "\t\tx\ty\tz\n";            

            drawingContext.DrawText(new FormattedText(pox,
                CultureInfo.GetCultureInfo("en-us"),
                FlowDirection.LeftToRight,
                new Typeface("Verdana"),
                10, System.Windows.Media.Brushes.White),
                //new Point(joint_x, joint_y));
                new Point(joint_x, joint_y));
            joint_y += 15;
            // Render Joints
            if (tableheaderfirst)
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(filepath, true))
                {
                    file.Write(",");
                    foreach (Joint joint in skeleton.Joints)
                    {
                        file.Write(string.Format(",{0},,", joint.JointType.ToString()));
                    }
                    file.Write("\nElapsed,");
                    for (int i=0; i<skeleton.Joints.Count; i++)
                    {
                        file.Write("x,y,z,");
                    }
                    file.Write("L_neck,L_shoulder,L_elbow,L_wrist,R_neck,R_shoulder,R_elbow,R_wrist\n");
                    
                    tableheaderfirst = false;
                }
            }
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(filepath, true))
            {
                file.Write(string.Format("{0:00},", stopWatch.ElapsedMilliseconds));
            }
            foreach (Joint joint in skeleton.Joints)
            {
                Brush drawBrush = null;

                if (joint.TrackingState == JointTrackingState.Tracked)
                {
                    drawBrush = this.trackedJointBrush;                    
                }
                else if (joint.TrackingState == JointTrackingState.Inferred)
                {
                    drawBrush = this.inferredJointBrush;                    
                }

                if (drawBrush != null)
                {
                    drawingContext.DrawEllipse(drawBrush, null, this.SkeletonPointToScreen(joint.Position), JointThickness, JointThickness);
                    
                    float x = joint.Position.X;
                    float y = joint.Position.Y;
                    float z = joint.Position.Z;

                    var type = joint.GetType();
                    string pos = "";
                    if (joint.JointType.ToString() == "Head" || joint.JointType.ToString() == "Waist" || joint.JointType.ToString() == "HipLeft")
                    {
                        pos = string.Format("{0}\t\t{1:N4}\t{2:N4}\t{3:N4}\n", joint.JointType.ToString(), x, y, z);
                    }
                    else
                    {
                        // Reverse enum lookup: A pain in the butt to find -----------------v
                        pos = string.Format("{0}\t{1:N4}\t{2:N4}\t{3:N4}\n", joint.JointType.ToString(), x, y, z);
                    }
                    string foo = string.Format("{0:N4},{1:N4},{2:N4},", x, y, z);
                    using (System.IO.StreamWriter file = new System.IO.StreamWriter(filepath, true))
                    {
                        file.Write(foo);
                    }
                    //Point joint_pos = this.SkeletonPointToScreen(joint.Position);
                    
                    drawingContext.DrawText(new FormattedText(pos,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White),
                        new Point(joint_x, joint_y));
                        //this.SkeletonPointToScreen(joint.Position));
                    joint_y += 15;
                }
            }
            //using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"D:\Kinect_clips\071415_clips\WriteLines2.csv", true))
            //{
            //    file.Write("\n");
            //}
            joint_y += 15;
            //Joint shoulder = skeleton.Joints.
            double right_neck_angle = AngleBetweenJoints(skeleton.Joints[JointType.Head], skeleton.Joints[JointType.ShoulderCenter], skeleton.Joints[JointType.ShoulderRight]);
            string right_neck_angle_text = string.Format("Right neck: {0:N4}", right_neck_angle);
            drawingContext.DrawText(new FormattedText(right_neck_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double right_shoulder_angle = AngleBetweenJoints(skeleton.Joints[JointType.ShoulderCenter], skeleton.Joints[JointType.ShoulderRight], skeleton.Joints[JointType.ElbowRight]);
            string right_shoulder_angle_text = string.Format("Right shoulder: {0:N4}", right_shoulder_angle);
            drawingContext.DrawText(new FormattedText(right_shoulder_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double right_elbow_angle = AngleBetweenJoints(skeleton.Joints[JointType.ShoulderRight], skeleton.Joints[JointType.ElbowRight], skeleton.Joints[JointType.WristRight]);
            string right_elbow_angle_text = string.Format("Right elbow: {0:N4}", right_elbow_angle);
            drawingContext.DrawText(new FormattedText(right_elbow_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double right_wrist_angle = AngleBetweenJoints(skeleton.Joints[JointType.ElbowRight], skeleton.Joints[JointType.WristRight], skeleton.Joints[JointType.HandRight]);
            string right_wrist_angle_text = string.Format("Right wrist: {0:N4}", right_wrist_angle);
            drawingContext.DrawText(new FormattedText(right_wrist_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
             joint_y += 15;
            //Joint shoulder = skeleton.Joints.
            double left_neck_angle = AngleBetweenJoints(skeleton.Joints[JointType.Head], skeleton.Joints[JointType.ShoulderCenter], skeleton.Joints[JointType.ShoulderLeft]);
            string left_neck_angle_text = string.Format("Left neck: {0:N4}", left_neck_angle);
            drawingContext.DrawText(new FormattedText(left_neck_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double left_shoulder_angle = AngleBetweenJoints(skeleton.Joints[JointType.ShoulderCenter], skeleton.Joints[JointType.ShoulderLeft], skeleton.Joints[JointType.ElbowLeft]);
            string left_shoulder_angle_text = string.Format("Left shoulder: {0:N4}", left_shoulder_angle);
            drawingContext.DrawText(new FormattedText(left_shoulder_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double left_elbow_angle = AngleBetweenJoints(skeleton.Joints[JointType.ShoulderLeft], skeleton.Joints[JointType.ElbowLeft], skeleton.Joints[JointType.WristLeft]);
            string left_elbow_angle_text = string.Format("Left elbow: {0:N4}", left_elbow_angle);
            drawingContext.DrawText(new FormattedText(left_elbow_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));
            joint_y += 15;
            double left_wrist_angle = AngleBetweenJoints(skeleton.Joints[JointType.ElbowLeft], skeleton.Joints[JointType.WristLeft], skeleton.Joints[JointType.HandLeft]);
            string left_wrist_angle_text = string.Format("Left wrist: {0:N4}", left_wrist_angle);
            drawingContext.DrawText(new FormattedText(left_wrist_angle_text,
                        CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight,
                        new Typeface("Verdana"),
                        10, System.Windows.Media.Brushes.White)
                        , new Point(20.0, joint_y));

            double[] angle_data = { left_neck_angle, left_shoulder_angle, left_elbow_angle, left_wrist_angle, right_neck_angle, right_shoulder_angle, right_elbow_angle, right_wrist_angle };
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(filepath, true))
            {
                foreach (double line in angle_data)
                {
                    // If the line doesn't contain the word 'Second', write the line to the file. 
                    file.Write(string.Format("{0:N4},", line));                   
                }
                //file.WriteLine("---------------------------------------------------------------------------------------");
                file.Write("\n");

            }
            
        }

        //private void DrawSkeletonsWithOrientations(Skeleton skeleton)
        //  {
        //      //foreach (Skeleton skeleton in skeletonData)
        //      //{
        //          if (skeleton.TrackingState == SkeletonTrackingState.Tracked)
        //          {
        //              foreach (BoneOrientation orientation in skeleton.BoneOrientations)
        //              {
        //                  // Display bone with Rotation using quaternion
        //                  DrawBonewithRotation(orientation.StartJoint, orientation.EndJoint, orientation.AbsoluteRotation.Quaternion);
        //                  // Display hierarchical rotation using matrix
        //                  DrawHierarchicalRotation(orientation.StartJoint, orientation.HierarchicalRotation.Matrix)
        //              }
        //          }
        //      //}
        // } 

        /// <summary>
        /// Return the angle between 3 Joints
        /// Regresa el ángulo interno dadas 3 Joints
        /// </summary>
        /// <param name="j1"></param>
        /// <param name="j2"></param>
        /// <param name="j3"></param>
        /// <returns></returns>
        public static double AngleBetweenJoints(Joint j1, Joint j2, Joint j3)
        {
            double Angulo = 0;
            double shrhX = j1.Position.X - j2.Position.X;
            double shrhY = j1.Position.Y - j2.Position.Y;
            double shrhZ = j1.Position.Z - j2.Position.Z;
            double hsl = vectorNorm(shrhX, shrhY, shrhZ);
            double unrhX = j3.Position.X - j2.Position.X;
            double unrhY = j3.Position.Y - j2.Position.Y;
            double unrhZ = j3.Position.Z - j2.Position.Z;
            double hul = vectorNorm(unrhX, unrhY, unrhZ);
            double mhshu = shrhX * unrhX + shrhY * unrhY + shrhZ * unrhZ;
            double x = mhshu / (hul * hsl);
            if (x != Double.NaN)
            {
                if (-1 <= x && x <= 1)
                {
                    double angleRad = Math.Acos(x);
                    Angulo = angleRad * (180.0 / Math.PI);
                }
                else
                    Angulo = 0;


            }
            else
                Angulo = 0;


            return Angulo;

        }


        /// <summary>
        /// Euclidean norm of 3-component Vector
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        private static double vectorNorm(double x, double y, double z)
        {

            return Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2) + Math.Pow(z, 2));

        }

        /// <summary>
        /// Maps a SkeletonPoint to lie within our render space and converts to Point
        /// </summary>
        /// <param name="skelpoint">point to map</param>
        /// <returns>mapped point</returns>
        private Point SkeletonPointToScreen(SkeletonPoint skelpoint)
        {
            // Convert point to depth space.  
            // We are not using depth directly, but we do want the points in our 640x480 output resolution.
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(skelpoint, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }

        /// <summary>
        /// Draws a bone line between two joints
        /// </summary>
        /// <param name="skeleton">skeleton to draw bones from</param>
        /// <param name="drawingContext">drawing context to draw to</param>
        /// <param name="jointType0">joint to start drawing from</param>
        /// <param name="jointType1">joint to end drawing at</param>
        private void DrawBone(Skeleton skeleton, DrawingContext drawingContext, JointType jointType0, JointType jointType1)
        {
            Joint joint0 = skeleton.Joints[jointType0];
            Joint joint1 = skeleton.Joints[jointType1];

            // If we can't find either of these joints, exit
            if (joint0.TrackingState == JointTrackingState.NotTracked ||
                joint1.TrackingState == JointTrackingState.NotTracked)
            {
                return;
            }

            // Don't draw if both points are inferred
            if (joint0.TrackingState == JointTrackingState.Inferred &&
                joint1.TrackingState == JointTrackingState.Inferred)
            {
                return;
            }

            // We assume all drawn bones are inferred unless BOTH joints are tracked
            Pen drawPen = this.inferredBonePen;
            if (joint0.TrackingState == JointTrackingState.Tracked && joint1.TrackingState == JointTrackingState.Tracked)
            {
                drawPen = this.trackedBonePen;
            }

            drawingContext.DrawLine(drawPen, this.SkeletonPointToScreen(joint0.Position), this.SkeletonPointToScreen(joint1.Position));
        }

        /// <summary>
        /// Handles the checking or unchecking of the seated mode combo box
        /// </summary>
        /// <param name="sender">object sending the event</param>
        /// <param name="e">event arguments</param>
        private void CheckBoxSeatedModeChanged(object sender, RoutedEventArgs e)
        {
            if (null != this.sensor)
            {
                if (this.checkBoxSeatedMode.IsChecked.GetValueOrDefault())
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
                }
                else
                {
                    this.sensor.SkeletonStream.TrackingMode = SkeletonTrackingMode.Default;
                }
            }
        }
    }
}