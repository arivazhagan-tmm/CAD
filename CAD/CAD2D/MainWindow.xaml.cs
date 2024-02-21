using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Microsoft.Win32;

namespace CAD2D;

#region class MainWindow --------------------------------------------------------------------------
/// <summary>Implements an interaction logic to design and draw the 2D entities/shapes</summary>
public partial class MainWindow : Window {
   #region Constructors ---------------------------------------------
   public MainWindow () {
      InitializeComponent ();
      Title = "2D CAD";
      (Height, Width) = (750, 800);
      Background = Brushes.Transparent;
      WindowStartupLocation = WindowStartupLocation.CenterScreen;
      WindowState = WindowState.Maximized;
      Loaded += OnLoaded;
   }
   #endregion

   #region Implementation -------------------------------------------
   void LoadDrawings (object sender, RoutedEventArgs e) {
      var dlg = new OpenFileDialog ();
      if (dlg.ShowDialog () is true) {
         ShowMessage ();
         using var reader = new StreamReader (dlg.FileName);
         while (!reader.EndOfStream) {
            var splits = reader?.ReadLine ()?.Split ("|");
            if (splits != null && splits.Length is 5 && Enum.TryParse (splits[0], out EEntityType type) &&
                splits[1].TryParse (out var startPt) && splits[2].TryParse (out var endPt)) {
               var layer = new BrushConverter ().ConvertFromString ($"{splits[^2]}") as SolidColorBrush;
               var lineWeight = double.Parse (splits[^1]);
               var pen = new Pen (layer, lineWeight);
               switch (type) {
                  case EEntityType.Line: mEntities.Add (new Line () { StartPoint = startPt, EndPoint = endPt, Pen = pen }); break;
                  case EEntityType.Rectangle: mEntities.Add (new Rectangle () { StartPoint = startPt, EndPoint = endPt, Pen = pen }); break;
                  case EEntityType.Circle: mEntities.Add (new Circle () { Center = startPt, StartPoint = startPt, EndPoint = endPt, Pen = pen }); break;
               }
            } else ShowMessage ("Invalid drawing data!");
         }
         InvalidateVisual ();
      }
   }

   void OnOptionClick (object sender, RoutedEventArgs e) {
      if (sender is Button btn && Enum.TryParse (btn.Content.ToString (), out mType)) {
         ShowMessage ($"{mType}");
         SetDrawing ();
      }
   }

   void OnLoaded (object sender, RoutedEventArgs e) {
      // Initializing fields
      mEntities = [];
      mPreviewPoints = [];
      mType = EEntityType.None;
      mLayer = Brushes.Transparent;
      mPen = new (Brushes.White, 1.0);
      mGridSize = 30;
      var gridLineWeight = 0.15;
      mGridPen1 = new (Brushes.LightSteelBlue, gridLineWeight);
      mGridPen2 = new (Brushes.LightSteelBlue, 2 * gridLineWeight);
      // Events
      KeyDown += (s, e) => {
         if (e.Key is Key.Escape) {
            mType = 0;
            mPreviewPoints.Clear ();
            ShowMessage ();
            mSnapPoint = new Point (0, 0);
            InvalidateVisual ();
         }
      };
      MouseMove += OnMouseMove;
      MouseLeftButtonDown += OnMouseLeftButtonDown;
      // Clears all the existing drawing from the model space
      var clearMenu = new MenuItem () { Header = "Clear" };
      clearMenu.Click += (s, e) => {
         mType = 0;
         mEntities.Clear ();
         mPreviewPoints.Clear ();
         InvalidateVisual ();
      };
      // Allows orthogonal line drawing
      var orthoMenu = new MenuItem () { Header = "Ortho", IsCheckable = true };
      orthoMenu.Checked += (s, e) => { mOrthoOn = true; };
      orthoMenu.Unchecked += (s, e) => { mOrthoOn = false; };
      // Shows the snap points of existing entities
      var snapMenu = new MenuItem () { Header = "Snap", IsCheckable = true };
      snapMenu.Checked += (s, e) => { mSnapOn = true; };
      snapMenu.Unchecked += (s, e) => { mSnapOn = false; };
      // Shows grid lines in the model space
      var gridMenu = new MenuItem () { Header = "Grid", IsCheckable = true };
      gridMenu.Checked += (s, e) => { mGridOn = true; };
      gridMenu.Unchecked += (s, e) => { mGridOn = false; };
      ContextMenu = new ContextMenu ();
      ContextMenu.Items.Add (orthoMenu);
      ContextMenu.Items.Add (snapMenu);
      ContextMenu.Items.Add (gridMenu);
      ContextMenu.Items.Add (new Separator ());
      ContextMenu.Items.Add (clearMenu);
      // Adding command bindings
      CommandBindings.Add (new (ApplicationCommands.Save, SaveDrawings, (s, e) => e.CanExecute = mEntities.Count != 0));
      CommandBindings.Add (new (ApplicationCommands.Open, LoadDrawings));
   }

   void OnMouseMove (object sender, MouseEventArgs e) {
      var pt = e.GetPosition (this);
      mCurrentMousePoint = pt;
      if (mType != 0 && mDrawing != null && mDrawing.Started) {
         if (mType is EEntityType.Square) {
            var p1 = mPreviewPoints[0];
            var quadrant = p1.Quadrant (pt);
            var theta = 45.0;
            switch (quadrant) {
               case EQuadrant.I: theta *= -3; break;
               case EQuadrant.II: theta *= -1; break;
               case EQuadrant.IV: theta *= 3; break;
            }
            pt = p1.RadialMove (p1.Distance (pt), theta);
            mUniquePoint = pt;
         }
         mPreviewPoints.Add (pt);
         if (mType is EEntityType.Sketch) mDrawing.ReceiveInput (pt);
      }
      if (mSnapOn) {
         mSnapPoint = new ();
         var refPoint = pt.ConvertTo2D ();
         foreach (var entity in mEntities) {
            if (refPoint.HasNearestPoint (entity.Vertices, 15, out var nearestPoint)) {
               mSnapPoint = nearestPoint.Convert ();
               break;
            }
         }
      }
      mCoordinatesTBlock.Text = $"X: {(int)mCurrentMousePoint.X}\tY: {(int)mCurrentMousePoint.Y}";
      InvalidateVisual ();
   }

   void OnMouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
      if (mType is 0 || mDrawing is null) return;
      ShowMessage ();
      var pt = e.GetPosition (this);
      if (!mSnapPoint.IsOrigin ()) pt = mSnapPoint;
      if (!mUniquePoint.IsOrigin ()) pt = mUniquePoint;
      if (mOrthoOn && mDrawing != null && mDrawing is LineDrawing lDWG && lDWG.Started) pt = mPreviewPoints[0].GetOrthoPoint (pt);
      mDrawing?.ReceiveInput (new Point2D (pt.X, pt.Y));
      if (mDrawing.Completed) {
         mEntities.Add (mDrawing.Entity);
         mPreviewPoints.Clear ();
         SetDrawing ();
         ShowMessage ();
      } else {
         ShowMessage (mDrawing.CurrentStep);
         mPreviewPoints.Add (pt);
      }
      InvalidateVisual ();
   }

   protected override void OnRender (DrawingContext dc) {
      // Showing grids in the model space
      if (mGridOn) {
         for (int i = 0; i < Width; i += mGridSize) {
            var tmp = i * 5;
            dc.DrawLine (mGridPen1, new (0, i), new (Width, i));
            dc.DrawLine (mGridPen2, new (0, tmp), new (Width, tmp));
            dc.DrawLine (mGridPen1, new (i, 0), new (i, Height));
            dc.DrawLine (mGridPen2, new (tmp, 0), new (tmp, Height));
         }
      }
      // Showing snap points in the model space
      if (mSnapOn) {
         var snapSize = 5;
         var (s1, s2) = (new Point (mSnapPoint.X - snapSize, mSnapPoint.Y - snapSize), new Point (mSnapPoint.X + snapSize, mSnapPoint.Y + snapSize));
         dc.DrawRectangle (Brushes.DarkOrange, mPen, new (s1, s2));
      }
      // Showing preview of the current drawing
      if (mType != 0 && mDrawing != null && !mDrawing.Completed && mPreviewPoints?.Count > 1) {
         var (p1, p2) = (mPreviewPoints[0], mPreviewPoints[^1]);
         switch (mDrawing) {
            case CircleDrawing:
               var radius = p1.Distance (p2);
               dc.DrawEllipse (mLayer, mPen, p1, radius, radius);
               dc.DrawLine (mPen, p1, p2);
               break;
            case LineDrawing:
               if (mOrthoOn) p2 = p1.GetOrthoPoint (p2);
               dc.DrawLine (mPen, p1, p2);
               break;
            case RectDrawing: dc.DrawRectangle (mLayer, mPen, new Rect (p1, p2)); break;
            case SketchDrawing:
               for (int i = 0, len = mPreviewPoints.Count - 1; i < len; i++) dc.DrawLine (mPen, mPreviewPoints[i], mPreviewPoints[i + 1]);
               break;
            case SquareDrawing:
               dc.DrawRectangle (Brushes.Transparent, mPen, new (p1, p2));
               dc.DrawLine (mPen, p1, p2);
               break;
         }
      }
      // Updating the model space with the existing entities
      if (mEntities != null && mEntities.Count > 0) {
         foreach (var entity in mEntities) {
            switch (entity) {
               case Circle c: dc.DrawEllipse (c.Layer, c.Pen, c.Center.Convert (), c.Radius, c.Radius); break;
               case Line l: dc.DrawLine (l.Pen, l.StartPoint.Convert (), l.EndPoint.Convert ()); break;
               case Rectangle r: dc.DrawRectangle (r.Layer, r.Pen, r.Rect); break;
               case Sketch s:
                  for (int i = 0, len = s.SketchPoints.Length - 1; i < len; i++) dc.DrawLine (s.Pen, s.SketchPoints[i], s.SketchPoints[i + 1]);
                  break;
               case Square sq: dc.DrawRectangle (sq.Layer, sq.Pen, sq.SQR); break;
            }
         }
      }
      base.OnRender (dc);
   }

   void SaveDrawings (object sender, RoutedEventArgs e) {
      if (mEntities.Count is 0) {
         ShowMessage ("Cannot save an empty drawing!");
         return;
      }
      var entityData = string.Empty;
      mEntities.ForEach (e => entityData += e);
      if (string.IsNullOrEmpty (entityData)) return;
      var dlg = new SaveFileDialog { DefaultExt = "txt", Title = "Export drawing", FileName = $"2D_Drawings" };
      if (dlg.ShowDialog () is true) {
         using var writer = new StreamWriter (dlg.FileName);
         writer.Write (entityData);
         ShowMessage ("File Saved!");
      }
   }

   void SetDrawing () {
      switch (mType) {
         case EEntityType.Line: mDrawing = new LineDrawing (); break;
         case EEntityType.Circle: mDrawing = new CircleDrawing (); break;
         case EEntityType.Rectangle: mDrawing = new RectDrawing (); break;
         case EEntityType.Sketch: mDrawing = new SketchDrawing (); break;
         case EEntityType.Square: mDrawing = new SquareDrawing (); break;
      }
      mUniquePoint = new ();
      ShowMessage (mDrawing.CurrentStep);
   }

   void ShowMessage (string message = "") => mPromptTBlock.Text = message;
   #endregion

   #region Private Data ---------------------------------------------
   int mGridSize;
   bool mGridOn, mSnapOn, mOrthoOn;
   Brush mLayer;
   Point mSnapPoint, mUniquePoint, mCurrentMousePoint;
   Drawing mDrawing;
   EEntityType mType;
   Pen mPen, mGridPen1, mGridPen2;
   List<Entity> mEntities;
   List<Point> mPreviewPoints;
   #endregion
}
#endregion

#region Enumerations ------------------------------------------------------------------------------
public enum EEntityType { None = 0, Arc, Circle, Line, Rectangle, Sketch, Square }

public enum EFeature { Ortho, Snap }

public enum EQuadrant { I, II, III, IV }
#endregion

#region Entities ----------------------------------------------------------------------------------

#region class Entity ------------------------------------------------------------------------------
/// <summary>A class to store design properties and features of the 2D entities</summary>
public class Entity {
   #region Constructors ---------------------------------------------
   public Entity () {
      mLineWeight = 2.0;
      mEntityID = Guid.NewGuid ();
      mLayer = Brushes.SteelBlue;
      mPen = new Pen (mLayer, mLineWeight);
   }
   #endregion

   #region Properties -----------------------------------------------
   public Pen Pen
   {
      get
      {
         if (mPen is null) mPen = new Pen (mLayer, mLineWeight);
         return mPen;
      }
      set => mPen = value;
   }
   public Brush Layer { get => mLayer; set => mLayer = value; }
   public Point2D StartPoint { get => mStartPt!; set => mStartPt = value; }
   public Point2D EndPoint { get => mEndPt!; set => mEndPt = value; }
   public Point2D Center { get => mCenter!; set => mCenter = value; }
   public double LineWeight { get => mLineWeight; set => mLineWeight = value; }
   public virtual double Area => double.NaN;
   public virtual Point2D[] Vertices
   {
      get
      {
         mVertices ??= [mStartPt, mEndPt];
         return mVertices;
      }
      private set => mVertices = value;
   }
   #endregion

   #region Private Data ---------------------------------------------
   protected Pen mPen;
   protected Brush mLayer;
   protected Guid mEntityID;
   protected bool mIsSelected;
   protected double mLineWeight;
   protected Point2D[] mVertices;
   protected Point2D mStartPt, mEndPt, mCenter;
   protected string mEntityData => $"|{mStartPt.X},{mStartPt.Y}|{mEndPt.X}, {mEndPt.Y}|{mPen.Brush}|{mPen.Thickness}\n";
   #endregion
}
#endregion

#region class Circle ------------------------------------------------------------------------------
public class Circle : Entity {
   #region Constructors ---------------------------------------------
   public Circle () : base () {
      mLayer = Brushes.Transparent;
   }
   #endregion

   #region Properties -----------------------------------------------
   public double Radius
   {
      get
      {
         if (mRadius is 0) mRadius = mCenter.Distance (mEndPt); return mRadius;

      }
   }
   public override double Area => Math.PI * Math.Pow (mRadius, 2);
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Circle{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   double mRadius;
   #endregion
}
#endregion

#region class Line --------------------------------------------------------------------------------
public class Line : Entity {
   #region Constructors ---------------------------------------------
   public Line () : base () { }
   #endregion

   #region Properties -----------------------------------------------
   public double Angle
   {
      get
      {
         if (double.IsNaN (mAngle)) mAngle = mStartPt.Angle (mEndPt);
         return mAngle;
      }
   }
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Line{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   double mAngle = double.NaN;
   #endregion
}
#endregion

#region class Rectangle ---------------------------------------------------------------------------
public class Rectangle : Entity {
   #region Constructors ---------------------------------------------
   public Rectangle () : base () {
      mLayer = Brushes.Transparent;
   }
   #endregion

   #region Properties -----------------------------------------------
   public Rect Rect
   {
      get
      {
         if (mRect.Width is 0) mRect = new Rect (mStartPt.Convert (), mEndPt.Convert ());
         return mRect;
      }
   }
   public double Width => mRect.Width;
   public double Height => mRect.Height;
   public override double Area => Width * Height;
   public override Point2D[] Vertices
   {
      get
      {
         var (dx, dy) = mStartPt.Diff (mEndPt);
         mVertices ??= [mStartPt, mEndPt, new (dx / 2, dy / 2), new (mStartPt.X, mEndPt.Y), new (mEndPt.X, mStartPt.Y)];
         return mVertices!;
      }
   }
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Rectangle{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   Rect mRect;
   #endregion
}
#endregion

#region class Sketch ------------------------------------------------------------------------------
public class Sketch : Entity {
   #region Constructors ---------------------------------------------
   public Sketch () : base () { }
   #endregion

   #region Properties -----------------------------------------------
   public Point[] SketchPoints { get; set; }
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Sketch{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   Point[] mSketchPoints;
   #endregion
}
#endregion

#region class Square ------------------------------------------------------------------------------
public class Square : Entity {
   #region Constructors ---------------------------------------------
   public Square () : base () {
      mLayer = Brushes.Transparent;
   }
   #endregion

   #region Properties -----------------------------------------------
   public Rect SQR
   {
      get
      {
         if (mSquare.Width is 0) mSquare = new Rect (mStartPt.Convert (), mEndPt.Convert ());
         return mSquare;
      }
   }
   public double Width => mSquare.Width;
   public double Height => mSquare.Height;
   public override double Area => Width * Height;
   public override Point2D[] Vertices
   {
      get
      {
         var (dx, dy) = mStartPt.Diff (mEndPt);
         mVertices ??= [mStartPt, mEndPt, new (dx / 2, dy / 2), new (mStartPt.X, mEndPt.Y), new (mEndPt.X, mStartPt.Y)];
         return mVertices!;
      }
   }
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Square{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   Rect mSquare;
   #endregion
}
#endregion

#endregion

#region Drawings ----------------------------------------------------------------------------------

#region class Drawing -----------------------------------------------------------------------------
/// <summary>A class that implements the systematic procedure to draw a 2D entity</summary>
public class Drawing {
   #region Properties -----------------------------------------------
   public Entity Entity => mEntity;
   public bool Started => mStartPoint != null;
   public bool Completed => Started && mEndPoint != null;
   public virtual string[] DrawingSteps
   {
      get
      {
         if(mDrawingSteps is null) mDrawingSteps = ["Select the start point", "Select the end point"];
         return mDrawingSteps;
      }
      private set { mDrawingSteps = value; }
   }
   public string CurrentStep => DrawingSteps[mStepIndex];
   #endregion

   #region Methods --------------------------------------------------
   public virtual void ReceiveInput (object obj) {
      if (obj is string str && Point2D.TryParse (str, out Point2D pt)) UpdatePoint (pt);
      else if (obj is Point2D point) UpdatePoint (point);
   }
   #endregion

   #region Implementation -------------------------------------------
   protected virtual void Execute () { }

   protected virtual void UpdatePoint (Point2D pt) {
      if (mStartPoint is null) { mStartPoint = pt; mStepIndex++; } else mEndPoint ??= pt;
      if (Completed) Execute ();
   }
   #endregion

   #region Private Data ---------------------------------------------
   protected int mStepIndex;
   protected Entity mEntity;
   protected string[] mDrawingSteps;
   protected Point2D mStartPoint, mEndPoint;
   #endregion
}
#endregion

#region class CircleDrawing -----------------------------------------------------------------------
public class CircleDrawing : Drawing {
   #region Constructors ---------------------------------------------
   public CircleDrawing () { }
   #endregion

   #region Properties -----------------------------------------------
   public override string[] DrawingSteps => ["Pick the center point", "Pick the tangent point"];
   #endregion

   #region Implementation -------------------------------------------
   protected override void Execute () {
      mEntity = new Circle () { StartPoint = mStartPoint, Center = mStartPoint, EndPoint = mEndPoint };
      base.Execute ();
   }
   #endregion
}
#endregion

#region class LineDrawing -------------------------------------------------------------------------
public class LineDrawing : Drawing {
   #region Constructors ---------------------------------------------
   public LineDrawing () { }
   #endregion

   #region Implementation -------------------------------------------
   protected override void Execute () {
      mEntity = new Line () { StartPoint = mStartPoint, EndPoint = mEndPoint };
      base.Execute ();
   }
   #endregion
}
#endregion

#region class RectDrawing -------------------------------------------------------------------------
public class RectDrawing : Drawing {
   #region Constructors ---------------------------------------------
   public RectDrawing () {}
   #endregion

   #region Implementation -------------------------------------------
   protected override void Execute () {
      mEntity = new Rectangle () { StartPoint = mStartPoint, EndPoint = mEndPoint };
      base.Execute ();
   }
   #endregion
}
#endregion

#region class SketchDrawing -----------------------------------------------------------------------
public class SketchDrawing : Drawing {
   #region Constructors ---------------------------------------------
   public SketchDrawing () {}
   #endregion

   #region Implementation -------------------------------------------
   public override void ReceiveInput (object obj) {
      if (obj is Point pt) mSketchPoints.Add (pt);
      base.ReceiveInput (obj);
   }

   protected override void Execute () {
      mEntity = new Sketch () { StartPoint = mStartPoint, EndPoint = mEndPoint, SketchPoints = [.. mSketchPoints] };
      base.Execute ();
   }
   #endregion

   #region Private Data ---------------------------------------------
   List<Point> mSketchPoints = [];
   #endregion
}
#endregion

#region class SquareDrawing -----------------------------------------------------------------------
public class SquareDrawing : Drawing {
   #region Constructors ---------------------------------------------
   public SquareDrawing () {}
   #endregion

   #region Implementation -------------------------------------------
   protected override void Execute () {
      mEntity = new Rectangle () { StartPoint = mStartPoint, EndPoint = mEndPoint };
      base.Execute ();
   }
   #endregion
}
#endregion

#endregion

#region class Point2D -----------------------------------------------------------------------------
/// <summary>A class to store the cartesian coordinates of a point</summary>
public class Point2D {
   #region Constructors ---------------------------------------------
   public Point2D (double x, double y) => (mX, mY) = (x, y);
   #endregion

   #region Properties -----------------------------------------------
   public double X { get => mX; set => mX = value; }
   public double Y { get => mY; set => mY = value; }
   public static Point2D Origin => new (0, 0);
   #endregion

   #region Methods --------------------------------------------------
   public double Angle (Point2D p) {
      var (dx, dy) = Diff (p);
      return Math.Atan (dy / dx) * (180 / Math.PI);
   }

   public Point Convert () => new (mX, mY);

   public (double DX, double DY) Diff (Point2D p) => (Math.Abs (mX - p.X), Math.Abs (mY - p.Y));

   public double Distance (Point2D pt) {
      var (dx, dy) = Diff (pt);
      return Math.Sqrt (Math.Pow (dx, 2) + Math.Pow (dy, 2));
   }

   public bool HasNearestPoint (Point2D[] pts, double delta, out Point2D nearestPoint) {
      nearestPoint = pts.ToList ().Find (p => p.Distance (this) <= delta);
      return nearestPoint != null;
   }

   public override string ToString () => $"( {mX}, {mY} )";

   public static bool TryParse (string str, out Point2D pt) {
      pt = Origin;
      if (str is null) return false;
      var split = str.Split (',');
      if (split is null) return false;
      if (!(double.TryParse (split[0], out var mX) && double.TryParse (split[1], out var mY))) return false;
      pt.X = mX; pt.Y = mY;
      return true;
   }
   #endregion

   #region Private Data ---------------------------------------------
   double mX, mY;
   #endregion
}
#endregion

#region class Utility -----------------------------------------------------------------------------
/// <summary>An utility class to assist the drawing of the entities</summary>
public static class Utility {
   public static double Angle (this Point p1, Point p2) => Math.Atan ((p1.Y - p2.Y) / (p1.X - p2.X)).ToDegrees ();

   public static Point2D ConvertTo2D (this Point pt) => new (pt.X, pt.Y);

   public static (double DX, double DY) Diff (this Point p1, Point p2) => (p1.X - p2.X, p1.Y - p2.Y);

   public static double Distance (this Point p1, Point p2) {
      var (dx, dy) = p1.Diff (p2);
      return Math.Sqrt (Math.Pow (dx, 2) + Math.Pow (dy, 2));
   }

   public static Point GetOrthoPoint (this Point p1, Point p2) {
      var ortho = new Point ();
      var angle = p1.Angle (p2);
      if (angle < 45) ortho = new (p2.X, p1.Y);
      else if (angle > 45) ortho = new (p1.X, p2.Y);
      return ortho;
   }

   public static bool IsOrigin (this Point pt) => pt.X is 0 && pt.Y is 0;

   public static Point RadialMove (this Point p, double distance, double theta) {
      theta = theta.ToRadians ();
      return new (p.X + (distance * Math.Cos (theta)), p.Y + (distance * Math.Sin (theta)));
   }

   public static bool TryParse (this string str, out Point2D p) {
      p = Point2D.Origin;
      var split = str.Split (',');
      if (split is null || split.Length < 2) return false;
      if (!double.TryParse (split[0], out var mX) || !double.TryParse (split[1], out var mY)) return false;
      p = new (mX, mY);
      return true;
   }

   public static double ToRadians (this double theta) => theta * Math.PI / 180;

   public static double ToDegrees (this double theta) => theta * 180 / Math.PI;

   public static EQuadrant Quadrant (this Point p, Point refPoint) {
      var (dx, dy) = p.Diff (refPoint);
      var quadrant = EQuadrant.I;
      if (dx < 0 && dy > 0) quadrant = EQuadrant.II;
      else if (dx < 0 && dy < 0) quadrant = EQuadrant.III;
      else if (dx > 0 && dy < 0) quadrant = EQuadrant.IV;
      return quadrant;
   }
}
#endregion