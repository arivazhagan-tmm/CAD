using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;

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
      DataContext = this;
   }
   #endregion

   #region Properties -----------------------------------------------
   public static MainWindow Viewport { get; private set; }
   /// <summary>Background layer of the user interface</summary>
   public Brush BGLayer { get => mBGLayer; set => mBGLayer = value; }
   /// <summary>Entities currently present in the viewport</summary>
   public List<Entity> Entities => mEntities;
   #endregion

   #region Implementation -------------------------------------------
   // Delete the selected entities from the viewport
   void Delete () {
      var entities = mEntities.SelectedEntities ();
      if (entities is null || entities.Length is 0) return;
      var deleteDwg = new Delete ();
      deleteDwg.ReceiveInput (entities);
      deleteDwg.ActualEntities.ForEach (Remove);
      mUndoStack.Push (deleteDwg);
      InvalidateVisual ();
   }

   // Initializing drawing of selected type
   void InitiateDrawing () {
      mAction = mType switch {
         EEntityType.Line => new DrawLine (),
         EEntityType.Circle => new DrawCircle (),
         EEntityType.PLine => new DrawPLine (),
         EEntityType.Rectangle => new DrawRectangle (),
         EEntityType.Sketch => new DrawScribble (),
         EEntityType.Square => new DrawSquare (),
         _ => new Clip (),
      };
      mFirstPoint = new ();
      ShowMessage (mType + ":\t" + mAction.CurrentStep);
   }

   // Loading the cad entity data from the specified format
   void Load (object sender, RoutedEventArgs e) {
      var dlg = new OpenFileDialog ();
      if (dlg.ShowDialog () is false) return;
      var loadDWG = new Load ();
      loadDWG.ReceiveInput (dlg.FileName);
      if (loadDWG.LoadedEntities.Count > 0) {
         mEntities.AddRange (loadDWG.LoadedEntities);
         mUndoStack.Push (loadDWG);
      }
      ShowMessage (loadDWG.LoadStatus);
      InvalidateVisual ();
   }

   void OnDrawSelection (object sender, RoutedEventArgs e) {
      if (sender is ToggleButton btn && Enum.TryParse (btn.Content.ToString (), out mType)) {
         mDrawOptionPanel.Children.OfType<ToggleButton> ().ToList ().ForEach (b => { if (b != btn) b.IsChecked = false; });
         ShowMessage ($"{mType}");
         InitiateDrawing ();
      }
   }

   void OnMouseMove (object sender, MouseEventArgs e) {
      var pt = e.GetPosition (this);
      if (mOrthoOn && mAction is DrawLine dwg && dwg.Started) pt = mFirstPoint.GetOrthoPoint (pt);
      mCurrentMousePoint = pt;
      ResetSnapPoint ();
      if (mSnapOn) {
         foreach (var entity in mEntities) {
            if (pt.HasNearestPoint (entity.Vertices, 15, out var nearestPoint)) {
               mSnapPoint = nearestPoint;
               break;
            }
         }
      }
      mCoordinatesTBlock.Text = $"X: {(int)mCurrentMousePoint.X}\tY: {(int)mCurrentMousePoint.Y}";
      InvalidateVisual ();
   }

   void OnMouseLeftButtonDown (object sender, MouseButtonEventArgs e) {
      ShowMessage ();
      var pt = e.GetPosition (this);
      if (mSnapOn && !mSnapPoint.IsAway ()) pt = mSnapPoint;
      if (mFirstPoint.IsOrigin ()) mFirstPoint = pt;
      if (mOrthoOn && mAction != null && mAction is DrawLine dwg && dwg.Started) pt = mFirstPoint.GetOrthoPoint (pt);
      mAction?.ReceiveInput (pt);
      if (mAction.Completed && mAction.CreatedEntity != null) {
         mEntities.Add (mAction.CreatedEntity);
         mUndoStack.Push (mAction);
         InitiateDrawing ();
      } else ShowMessage (mType + ":\t" + mAction.CurrentStep);
      InvalidateVisual ();
   }

   void OnMouseRightButtonDown (object sender, MouseButtonEventArgs e) {
      if (mAction is DrawPLine dwg && dwg.Started) {
         mAction?.ReceiveInput ("Completed");
         if (mAction.Completed) {
            mEntities.Add (mAction.CreatedEntity);
            mUndoStack.Push (mAction);
            InitiateDrawing ();
         } else ShowMessage (mType + ":\t" + mAction.CurrentStep);
         InvalidateVisual ();
      }
   }

   void OnLoaded (object sender, RoutedEventArgs e) {
      // Initializing fields
      mEntities = [];
      mType = EEntityType.None;
      mGridLayer = Brushes.DimGray;
      mBGLayer = new SolidColorBrush (Color.FromArgb (255, 200, 200, 200));
      mPen = new (Brushes.ForestGreen, 1.0);
      mGridSize = 30;
      var gridLineWeight = 0.15;
      mGridPen1 = new (mGridLayer, gridLineWeight);
      mGridPen2 = new (mGridLayer, 2 * gridLineWeight);
      mUndoStack = new ();
      mRedoStack = new ();
      // Events
      KeyDown += (s, e) => {
         if (e.Key is Key.Escape) {
            mType = 0;
            if (mEntities.Count > 0) mEntities.ForEach (e => e.Selected = false);
            mDrawOptionPanel.Children.OfType<ToggleButton> ().ToList ().ForEach (b => { b.IsChecked  = false; b.Focusable = false; });
            ShowMessage ();
            ResetSnapPoint ();
            InitiateDrawing ();
            InvalidateVisual ();
         }
      };
      MouseMove += OnMouseMove;
      MouseLeftButtonDown += OnMouseLeftButtonDown;
      MouseRightButtonDown += OnMouseRightButtonDown;
      // Clears all the existing drawing from the model space
      var clearMenu = new MenuItem () { Header = "Clear" };
      clearMenu.Click += (s, e) => {
         mType = 0;
         mEntities.Clear ();
         mUndoStack.Clear ();
         mRedoStack.Clear ();
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
      CommandBindings.Add (new (ApplicationCommands.Save, Save, (s, e) => e.CanExecute = mEntities.Count != 0));
      CommandBindings.Add (new (ApplicationCommands.SaveAs, (s, e) => { mFormat = EFileExtension.Bin; Save (s, e); }, (s, e) => e.CanExecute = mEntities.Count != 0));
      CommandBindings.Add (new (ApplicationCommands.Open, Load));
      CommandBindings.Add (new (ApplicationCommands.Delete, (s, e) => { Delete (); }, (s, e) => e.CanExecute = mEntities.Any (e => e.Selected)));
      CommandBindings.Add (new (ApplicationCommands.SelectAll, (s, e) => { mEntities.ForEach (e => e.Selected = true); InvalidateVisual (); }, (s, e) => e.CanExecute = mEntities.Count > 0));
      CommandBindings.Add (new (ApplicationCommands.Undo, (s, e) => Undo (), (s, e) => e.CanExecute = mEntities.Count > 0 || mAction != null));
      CommandBindings.Add (new (ApplicationCommands.Redo, (s, e) => Redo (), (s, e) => e.CanExecute = mRedoStack.Count > 0));
      Viewport = this;
      InitiateDrawing ();
   }

   protected override void OnRender (DrawingContext dc) {
      // Showing grids in the model space
      dc.DrawRectangle (mBGLayer, mPen, new Rect (new (-1, -1), new Point (ActualWidth, ActualHeight)));
      if (mGridOn) {
         for (int i = 0; i < ActualWidth; i += mGridSize) {
            var j = i * 5;
            dc.DrawLine (mGridPen1, new (0, i), new (ActualWidth, i));
            dc.DrawLine (mGridPen2, new (0, j), new (ActualWidth, j));
            dc.DrawLine (mGridPen1, new (i, 0), new (i, ActualHeight));
            dc.DrawLine (mGridPen2, new (j, 0), new (j, ActualHeight));
         }
      }
      // Showing snap points in the model space
      if (mSnapOn) {
         var snapSize = 5;
         var (s1, s2) = (new Point (mSnapPoint.X - snapSize, mSnapPoint.Y - snapSize), new Point (mSnapPoint.X + snapSize, mSnapPoint.Y + snapSize));
         dc.DrawRectangle (Brushes.LightBlue, mPen, new (s1, s2));
      }
      // Showing preview of the current drawing
      if (mAction != null && !mAction.Completed) {
         mAction.DrawingContext = dc;
         mAction.ReceivePreviewInput (mCurrentMousePoint);
      }
      // Updating the model space with the existing entities
      if (mEntities != null && mEntities.Count > 0) {
         foreach (var entity in mEntities) {
            switch (entity) {
               case Circle c: dc.DrawEllipse (c.Layer, c.Pen, c.Center, c.Radius, c.Radius); break;
               case Line l: dc.DrawLine (l.Pen, l.StartPoint, l.EndPoint); break;
               case PLine pl:
                  for (int i = 0, len = pl.PLinePoints.Length - 1; i < len; i++) dc.DrawLine (pl.Pen, pl.PLinePoints[i], pl.PLinePoints[i + 1]);
                  break;
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

   void ResetSnapPoint () => mSnapPoint = new (-10, -10);

   void Redo () {
      if (mRedoStack.Count is 0) return;
      var action = mRedoStack.Pop ();
      if (action is IEditDrawing editDwg) editDwg.EditedEntities.ForEach (Remove);
      else if (action is Load loadDwg) mEntities.AddRange (loadDwg.LoadedEntities);
      else mEntities.Add (action.CreatedEntity);
      mUndoStack.Push (action);
      InvalidateVisual ();
   }

   // Removing the given entity from the viewport
   void Remove (Entity entity) {
      if (entity == null && !mEntities.Contains (entity)) return;
      mEntities.Remove (entity);
   }

   void Undo () {
      if (mUndoStack.Count is 0) return;
      var action = mUndoStack.Pop ();
      if (action is IEditDrawing editDwg) mEntities.AddRange (editDwg.ActualEntities);
      else if (action is Load loadDwg) loadDwg.LoadedEntities.ForEach (Remove);
      else Remove (action.CreatedEntity);
      mRedoStack.Push (action);
      InvalidateVisual ();
   }

   // Saving the cad entity data to the specified format
   void Save (object sender, RoutedEventArgs e) {
      if (mEntities.Count is 0) {
         ShowMessage ("Cannot save an empty drawing!");
         return;
      }
      var saveDWG = new Save (mFormat);
      saveDWG.ReceiveInput (mEntities);
      ShowMessage (saveDWG.SaveStatus);
   }

   void ShowMessage (string message = "") => mPromptTBlock.Text = message;
   #endregion

   #region Private Data ---------------------------------------------
   int mGridSize;
   ICadAction mAction;
   EEntityType mType;
   EFileExtension mFormat;
   List<Entity> mEntities;
   Brush mBGLayer, mGridLayer;
   Pen mPen, mGridPen1, mGridPen2;
   bool mGridOn, mSnapOn, mOrthoOn;
   Point mSnapPoint, mFirstPoint, mCurrentMousePoint;
   Stack<ICadAction> mUndoStack, mRedoStack;
   #endregion
}
#endregion

#region Enumerations ------------------------------------------------------------------------------
/// <summary>The file extensions to be saved or loaded</summary>
public enum EFileExtension { Txt, Bin }
/// <summary>Types of two dimensional entities to be drawn</summary>
public enum EEntityType { None = 0, Arc, Circle, Line, PLine, Rectangle, Sketch, Square }
/// <summary>The quadrants of a point in a cartesian coordinate</summary>
public enum EQuadrant { I, II, III, IV }
/// <summary>Line types</summary>
public enum ELineType { Axis }
#endregion

#region Interfaces --------------------------------------------------------------------------------

#region interface ICadAction ----------------------------------------------------------------------
public interface ICadAction {
   #region Properties -----------------------------------------------
   public bool Started { get; }
   public bool Completed { get; }
   public string CurrentStep { get; }
   public virtual string[] Steps { set { } }
   public Entity CreatedEntity { get; }
   public DrawingContext DrawingContext { set; }
   #endregion

   #region Methods --------------------------------------------------
   public virtual void Execute () { }
   public virtual void ReceiveInput (object obj) { }
   public virtual void ReceivePreviewInput (object obj) { }
   #endregion
}
#endregion

#region interface IEditDrawing --------------------------------------------------------------------
public interface IEditDrawing {
   public List<Entity> ActualEntities { get; }
   public List<Entity> EditedEntities { get; }
}
#endregion

#region interface IPolygon ------------------------------------------------------------------------
public interface IPolygon {
   #region Properties -----------------------------------------------
   public double Perimeter { get; }
   public double Area { get; }
   public virtual int NumberOfSides { get => 0; }
   public Point[] ConvexHull { get; }
   #endregion
}
#endregion

#endregion

#region struct Bound ------------------------------------------------------------------------------
public struct Bound {
   #region Constructors ---------------------------------------------
   public Bound (Point p1, Point p2) => (mMin, mMax) = (p1, p2);
   #endregion

   #region Properties -----------------------------------------------
   public readonly Point Min => mMin;
   public readonly Point Max => mMax;
   #endregion

   #region Private Data ---------------------------------------------
   Point mMin, mMax;
   #endregion
}
#endregion

#region Draw Entities -----------------------------------------------------------------------------

#region class DrawCircle --------------------------------------------------------------------------
public class DrawCircle : CadAction {
   #region Constructors ---------------------------------------------
   public DrawCircle () : base () { }
   #endregion

   #region Properties -----------------------------------------------
   public override string[] Steps => ["Pick the center point", "Pick the tangent point"];
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      if (!Completed) return;
      mEntity = new Circle (mStartPoint, mEndPoint);
      base.Execute ();
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      var radius = previewPoint.Distance (mStartPoint);
      DrawingContext.DrawEllipse (mFillLayer, mDrawingPen, mStartPoint, radius, radius);
      base.ReceivePreviewInput (obj);
   }
   #endregion
}
#endregion

#region class DrawLine ----------------------------------------------------------------------------
public class DrawLine : CadAction {
   #region Constructors ---------------------------------------------
   public DrawLine () : base () { }
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      if (!Completed) return;
      mEntity = new Line (mStartPoint, mEndPoint);
      base.Execute ();
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      DrawingContext.DrawLine (mDrawingPen, mStartPoint, previewPoint);
      base.ReceivePreviewInput (obj);
   }
   #endregion

   #region Private Data ---------------------------------------------
   ELineType mLineType;
   #endregion
}
#endregion

#region class DrawPLine ---------------------------------------------------------------------------
public class DrawPLine : CadAction {
   #region Constructors ---------------------------------------------
   public DrawPLine () : base () {
      mCadSteps = ["Select the first point", "Select the next point", "Right click to finish the polyline"];
   }
   #endregion

   #region Properties -----------------------------------------------
   public override bool Completed => mPLineFormed;
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      mEntity = new PLine () { StartPoint = mStartPoint, EndPoint = mPLinePoints[^1], PLinePoints = [.. mPLinePoints] };
      base.Execute ();
   }

   public override void ReceiveInput (object obj) {
      if (obj is string str && str is "Completed") {
         mPLineFormed = true;
         Execute ();
      } else base.ReceiveInput (obj);
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      for (int i = 0, len = mPLinePoints.Count; i < len - 1; i++) DrawingContext.DrawLine (mDrawingPen, mPLinePoints[i], mPLinePoints[i + 1]);
      DrawingContext.DrawLine (mDrawingPen, mPLinePoints[^1], previewPoint);
      base.ReceivePreviewInput (obj);
   }
   #endregion

   #region Implementation -------------------------------------------
   protected override void AssignPoint (Point pt) {
      mPLinePoints.Add (pt);
      base.AssignPoint (pt);
   }
   #endregion

   #region Private Data ---------------------------------------------
   bool mPLineFormed;
   ELineType mLineType;
   List<Point> mPLinePoints = [];
   #endregion
}
#endregion

#region class DrawRectangle -----------------------------------------------------------------------
public class DrawRectangle : CadAction {
   #region Constructors ---------------------------------------------
   public DrawRectangle () : base () { }
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      if (!Completed) return;
      mEntity = new Rectangle (mStartPoint, mEndPoint);
      base.Execute ();
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      DrawingContext.DrawRectangle (mFillLayer, mDrawingPen, new (mStartPoint, previewPoint));
      base.ReceivePreviewInput (obj);
   }
   #endregion
}
#endregion

#region class DrawScribble ------------------------------------------------------------------------
public class DrawScribble : CadAction {
   #region Constructors ---------------------------------------------
   public DrawScribble () { }
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      if (!Completed) return;
      mEntity = new Sketch () { StartPoint = mStartPoint, EndPoint = mEndPoint, SketchPoints = [.. mSketchPoints] };
      base.Execute ();
   }

   public override void ReceiveInput (object obj) {
      if (obj is Point pt) mSketchPoints.Add (pt);
      base.ReceiveInput (obj);
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      mSketchPoints.Add (previewPoint);
      for (int i = 0, len = mSketchPoints.Count; i < len - 1; i++) DrawingContext.DrawLine (mDrawingPen, mSketchPoints[i], mSketchPoints[i + 1]);
      base.ReceivePreviewInput (obj);
   }
   #endregion

   #region Private Data ---------------------------------------------
   List<Point> mSketchPoints = [];
   #endregion
}
#endregion

#region class DrawSquare --------------------------------------------------------------------------
public class DrawSquare : CadAction {
   #region Constructors ---------------------------------------------
   public DrawSquare () { }
   #endregion

   #region Methods --------------------------------------------------
   public override void Execute () {
      mEntity = new Rectangle (mStartPoint, mEndPoint);
      base.Execute ();
   }

   public override void ReceiveInput (object obj) {
      if (Started && !mDiagonalPoint.IsOrigin ()) mEndPoint = mDiagonalPoint;
      base.ReceiveInput (obj);
   }

   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      var p1 = mStartPoint;
      var quadrant = mStartPoint.Quadrant (previewPoint);
      var theta = 45.0;
      switch (quadrant) {
         case EQuadrant.I: theta *= -3; break;
         case EQuadrant.II: theta *= -1; break;
         case EQuadrant.IV: theta *= 3; break;
      }
      previewPoint = p1.RadialMove (p1.Distance (previewPoint), theta);
      mDiagonalPoint = previewPoint;
      DrawingContext.DrawRectangle (mFillLayer, mDrawingPen, new (mStartPoint, mDiagonalPoint));
      base.ReceivePreviewInput (obj);
   }
   #endregion

   #region Private Data ---------------------------------------------
   Point mDiagonalPoint;
   #endregion
}
#endregion

#endregion

#region Entities ----------------------------------------------------------------------------------

#region class Entity ------------------------------------------------------------------------------
/// <summary>A class to store design properties and features of the 2D entities</summary>
public class Entity {
   #region Constructors ---------------------------------------------
   public Entity () {
      mLineWeight = 1.0;
      mEntityID = Guid.NewGuid ();
      mLayer = Brushes.Black;
      mPen = new Pen (mLayer, mLineWeight);
   }
   #endregion

   #region Properties -----------------------------------------------
   public Pen Pen
   {
      get => mPen;
      set => mPen = value;
   }
   public Brush Layer { get => mLayer; set => mLayer = value; }
   public Point StartPoint { get => mStartPt!; set => mStartPt = value; }
   public Point EndPoint { get => mEndPt!; set => mEndPt = value; }
   public Point Center { get => mCenter!; set => mCenter = value; }
   public double LineWeight { get => mLineWeight; set => mLineWeight = value; }
   public virtual Point[] Vertices
   {
      get
      {
         mVertices ??= [mStartPt, mEndPt];
         return mVertices;
      }
      private set => mVertices = value;
   }
   public bool Selected
   {
      get => mIsSelected;
      set
      {
         mIsSelected = value;
         mPen = mIsSelected ? new Pen (Brushes.GhostWhite, mLineWeight * 2) : new Pen (Brushes.Black, mLineWeight);
      }
   }
   public Guid EntityID => mEntityID;
   #endregion

   #region Private Data ---------------------------------------------
   protected Pen mPen;
   protected Brush mLayer;
   protected Guid mEntityID;
   protected bool mIsSelected;
   protected double mLineWeight;
   protected Point[] mVertices;
   protected Point mStartPt, mEndPt, mCenter;
   protected string mEntityData => $"{mStartPt.Convert ()}{mEndPt.Convert ()}|{mPen.Brush}|{mPen.Thickness}\n";
   #endregion
}
#endregion

#region class Circle ------------------------------------------------------------------------------
public class Circle : Entity, IPolygon {
   #region Constructors ---------------------------------------------
   public Circle (Point startPt, Point endPt) : base () {
      (mCenter, mStartPt, mEndPt) = (startPt, startPt, endPt);
      mLayer = Brushes.Transparent;
   }
   #endregion

   #region Properties -----------------------------------------------
   public double Diameter => 2 * Radius;
   public double Radius
   {
      get
      {
         if (mRadius is 0) mRadius = mCenter.Distance (mEndPt); return mRadius;

      }
   }
   public double Area => Math.PI * Math.Pow (mRadius, 2);
   public double Perimeter => Math.PI * Diameter;
   public Point[] ConvexHull => [];
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"Circle{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   double mRadius, mArea, mPerimeter;
   #endregion
}
#endregion

#region class Line --------------------------------------------------------------------------------
public class Line : Entity {
   #region Constructors ---------------------------------------------
   public Line (Point startPt, Point endPt, Pen pen = null) : base () {
      (mStartPt, mEndPt) = (startPt, endPt);
      if (pen != null) mPen = pen;
   }
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

#region class PLine -------------------------------------------------------------------------------
public class PLine : Entity {
   #region Constructors ---------------------------------------------
   public PLine () : base () { }
   #endregion

   #region Properties -----------------------------------------------
   public Point[] PLinePoints { get => mPLinePoints; set => mPLinePoints = value; }

   public override Point[] Vertices => PLinePoints;
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () => $"PLine{mEntityData}";
   #endregion

   #region Private Data ---------------------------------------------
   Point[] mPLinePoints;
   #endregion
}
#endregion

#region class Rectangle ---------------------------------------------------------------------------
public class Rectangle : Entity, IPolygon {
   #region Constructors ---------------------------------------------
   public Rectangle (Point startPt, Point endPt) : base () {
      (StartPoint, EndPoint) = (startPt, endPt);
      mLayer = Brushes.Transparent;
   }
   #endregion

   #region Properties -----------------------------------------------
   public Rect Rect
   {
      get
      {
         if (mRect.Width is 0) mRect = new Rect (mStartPt, mEndPt);
         return mRect;
      }
   }
   public double Width => mRect.Width;
   public double Height => mRect.Height;
   public double Area => Width * Height;
   public override Point[] Vertices
   {
      get
      {
         mVertices ??= [mStartPt, mEndPt, new (mStartPt.X, mEndPt.Y), new (mEndPt.X, mStartPt.Y)];
         return mVertices!;
      }
   }
   public double Perimeter => 2 * (Height + Width);
   public int NumberOfSides => 4;
   public Point[] ConvexHull => mVertices;
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
   public Point[] SketchPoints { get => mSketchPoints; set => mSketchPoints = value; }
   #endregion

   #region Methods --------------------------------------------------
   public override string ToString () {
      var pts = string.Empty;
      mSketchPoints.ToList ().ForEach (pt => pts += pt.Convert ());
      return $"Sketch{pts}|{mLayer}|{mLineWeight}";
   }
   #endregion

   #region Private Data ---------------------------------------------
   Point[] mSketchPoints;
   #endregion
}
#endregion

#region class Square ------------------------------------------------------------------------------
public class Square : Entity, IPolygon {
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
         if (mSquare.Width is 0) mSquare = new Rect (mStartPt, mEndPt);
         return mSquare;
      }
   }
   public double Side => mSquare.Width;
   public double Area => Side * Side;
   public override Point[] Vertices
   {
      get
      {
         var (dx, dy) = mStartPt.Diff (mEndPt);
         mVertices ??= [mStartPt, mEndPt, new (dx / 2, dy / 2), new (mStartPt.X, mEndPt.Y), new (mEndPt.X, mStartPt.Y)];
         return mVertices!;
      }
   }
   public double Perimeter => 4 * Side;
   public int NumberOfSides => 4;
   public Point[] ConvexHull => mVertices;
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

#region Entity Handling----------------------------------------------------------------------------

#region class CadAction ---------------------------------------------------------------------------
/// <summary>A class that implements the systematic procedure to draw a 2D entity</summary>
public class CadAction : ICadAction {
   #region Constructors ---------------------------------------------
   public CadAction () {
      mDrawingLayer = Brushes.ForestGreen;
      mFillLayer = Brushes.Transparent;
      mDrawingPen = new (mDrawingLayer, 0.5);
   }
   #endregion

   #region Properties -----------------------------------------------
   public Entity CreatedEntity => mEntity;
   public bool Started => !mStartPoint.IsOrigin ();
   public virtual bool Completed => Started && !mEndPoint.IsOrigin ();
   public bool CanViewPreview => Started && DrawingContext != null;
   public virtual string[] Steps
   {
      get
      {
         mCadSteps ??= ["Select the start point", "Select the end point"];
         return mCadSteps;
      }
      private set { mCadSteps = value; }
   }
   public string CurrentStep
   {
      get
      {
         if (Steps != null && Steps.Length > 0) return Steps[mStepIndex];
         return string.Empty;
      }
   }
   public DrawingContext DrawingContext { get; set; }
   #endregion

   #region Methods --------------------------------------------------
   /// <summary>Tries to create a point from the given object based on its type</summary>
   public virtual void ReceiveInput (object obj) {
      if (obj is string str && str.TryParse (out var pt)) AssignPoint (pt);
      else if (obj is Point point) AssignPoint (point);
   }

   public virtual void ReceivePreviewInput (object obj) { }

   public virtual void Execute () { }
   #endregion

   #region Implementation -------------------------------------------
   // Assigns the given point to the either start or end point
   protected virtual void AssignPoint (Point pt) {
      if (mStartPoint.IsOrigin ()) { mStartPoint = pt; mStepIndex++; } else if (mEndPoint.IsOrigin ()) { mEndPoint = pt; }
      if (Completed) Execute ();
   }
   #endregion

   #region Private Data ---------------------------------------------
   protected int mStepIndex;
   protected Entity mEntity;
   protected Pen mDrawingPen;
   protected string[] mCadSteps;
   protected List<Entity> mEntities;
   protected Brush mDrawingLayer, mFillLayer;
   protected Point mStartPoint, mEndPoint, mPreviewPoint;
   #endregion
}
#endregion

#region class Clip --------------------------------------------------------------------------------
public class Clip : CadAction {
   #region Constructors ---------------------------------------------
   /// <summary>Assigns the clipping pen and background</summary>
   public Clip () {
      mDrawingPen = new Pen (Brushes.SteelBlue, 1);
      mFillLayer = Brushes.LightSteelBlue;
      mCadSteps = [];
   }
   #endregion

   #region Methods --------------------------------------------------
   /// <summary>Sets the selection status of entities from the viewport if they lies inside the clip bound</summary>
   public override void Execute () {
      if (!Completed) return;
      var (clip1, clip2) = (new Bound (mStartPoint, mEndPoint), new Bound (mEndPoint, mStartPoint));
      var entities = MainWindow.Viewport.Entities.Where (e => e.IsInside (clip1) || e.IsInside (clip2));
      if (entities.Any ()) entities.ToList ().ForEach (e => e.Selected = true);
      base.Execute ();
   }

   /// <summary>Forms the preview clip rectangle</summary>
   public override void ReceivePreviewInput (object obj) {
      if (!CanViewPreview || obj is not Point previewPoint) return;
      DrawingContext.DrawRectangle (mFillLayer, mDrawingPen, new (mStartPoint, previewPoint));
      base.ReceivePreviewInput (obj);
   }
   #endregion
}
#endregion

#region class Delete ------------------------------------------------------------------------------
public class Delete : CadAction, IEditDrawing {
   #region Properties -----------------------------------------------
   /// <summary>Unaltered entities in the viewport</summary>
   public List<Entity> ActualEntities => mActualEntities;
   /// <summary>Deleted entities from the viewport</summary>
   public List<Entity> EditedEntities => mDeletedEntities;
   #endregion

   #region Methods --------------------------------------------------
   /// <summary>Receives the cad entities as object and stores them in the fields</summary>
   public override void ReceiveInput (object obj) {
      if (obj is not IEnumerable<Entity> entities || entities.Count () is 0) return;
      mActualEntities = mDeletedEntities = entities.ToList ();
      base.ReceiveInput (obj);
   }
   #endregion

   #region Private Data ---------------------------------------------
   List<Entity> mActualEntities, mDeletedEntities;
   #endregion
}
#endregion

#region class Load --------------------------------------------------------------------------------
public class Load : CadAction {
   #region Properties -----------------------------------------------
   /// <summary>Status as a string whether the entities are loaded or not</summary>
   public string LoadStatus => mLoadStatus;
   /// <summary>Entities formed from the given file data</summary>
   public List<Entity> LoadedEntities => mEntities;
   #endregion

   #region Methods --------------------------------------------------
   /// <summary>Parses the data into to the cad entity properties</summary>
   /// Creates the entities if the valid properties are found.
   /// Returns the parsing corresponding error message if the parsing fails
   public override void Execute () {
      using var reader = new StreamReader (mFileName);
      mEntities = [];
      while (!reader.EndOfStream) {
         var s = reader?.ReadLine ()?.Split ("|");
         if (s is null || s.Length < 5 || !Enum.TryParse (s[0], out EEntityType type)) { mLoadStatus = "Invalid drawing data!"; return; }
         if (!s[1..^2].TryParse (out var pts) || pts.Length is < 2) { mLoadStatus = "Cannot read the entity data!"; return; }
         var layer = new BrushConverter ().ConvertFromString ($"{s[^2]}") as SolidColorBrush;
         var lineWeight = double.Parse (s[^1]);
         var pen = new Pen (layer, lineWeight);
         var (startPt, endPt) = (pts[0], pts[^1]);
         switch (type) {
            case EEntityType.Circle: mEntity = new Circle (startPt, endPt) { Pen = pen }; break;
            case EEntityType.Line: mEntity = new Line (startPt, endPt, pen); break;
            case EEntityType.PLine: mEntity = new PLine () { Pen = pen, PLinePoints = pts }; break;
            case EEntityType.Rectangle: mEntity = new Rectangle (startPt, endPt) { Pen = pen }; break;
            case EEntityType.Sketch: mEntity = new Sketch () { Pen = pen, SketchPoints = pts }; break;
         }
         mEntities.Add (mEntity);
      }
      mLoadStatus = "Loaded!";
      base.Execute ();
   }

   /// <summary>Initializes the parsing of data present in the given location to cad entities</summary>
   /// Checks the file name and file format. 
   /// If invalid data found returns the corresponding status
   public override void ReceiveInput (object obj) {
      if (obj is not string fileName) return;
      if (string.IsNullOrEmpty (fileName) || !File.Exists (fileName)) { mLoadStatus = "File not exists!"; return; }
      if (!Enum.TryParse (fileName[^3..].ToLower (), ignoreCase: true, out mFileType)) { mLoadStatus = "Invalid file format!"; return; }
      mFileName = fileName;
      Execute ();
      base.ReceiveInput (obj);
   }
   #endregion

   #region Private Data ---------------------------------------------
   string mFileName, mLoadStatus;
   EFileExtension mFileType;
   #endregion
}
#endregion

#region class Save --------------------------------------------------------------------------------
public class Save : CadAction {
   #region Constructors ---------------------------------------------
   public Save (EFileExtension fileFormat) => mFormat = fileFormat;
   #endregion

   #region Properties -----------------------------------------------
   /// <summary>Returns if saved file exists in the selected directory</summary>
   public bool DrawingSaved => mDrawingSaved;
   /// <summary>Returns the status of the save action</summary>
   public string SaveStatus => mSaveStatus;
   #endregion

   #region Methods --------------------------------------------------
   /// <summary>Initializes saving the file in the selected directory and the selected format</summary>
   public override void Execute () {
      if (mFormat is EFileExtension.Bin) {
         using BinaryWriter binWriter = new (File.Open (mFileName, FileMode.Create), Encoding.ASCII);
         binWriter.Write (100.3125);
      } else {
         using var writer = new StreamWriter (mFileName);
         writer.Write (mEntityData);
      }
      mDrawingSaved = File.Exists (mFileName);
      if (mDrawingSaved) mSaveStatus = "Drawing Saved!";
      base.Execute ();
   }

   /// <summary>Creates the entity data from the given entities and shows the dialog box to store the data</summary>
   /// Sets the saving status based on the entity data
   public override void ReceiveInput (object obj) {
      if (obj is not List<Entity> entities) return;
      mEntityData = string.Empty;
      entities.ForEach (e => mEntityData += e);
      if (string.IsNullOrEmpty (mEntityData)) {
         mSaveStatus = "Invalid drawing data!";
         return;
      }
      var dlg = new SaveFileDialog { DefaultExt = $"{mFormat}".ToLower (), Title = "Save drawing", FileName = $"2D_Drawing" };
      if (dlg.ShowDialog () is true) {
         mFileName = dlg.FileName;
         Execute ();
      }
      base.ReceiveInput (obj);
   }
   #endregion

   #region Implementation -------------------------------------------
   byte[] ToBinary () {
      return null;
   }
   #endregion

   #region Private Data ---------------------------------------------
   bool mDrawingSaved;
   string mEntityData, mFileName, mSaveStatus;
   EFileExtension mFormat;
   #endregion
}
#endregion

#endregion

#region class Utility -----------------------------------------------------------------------------
/// <summary>An utility class to assist the drawing of the entities</summary>
public static class Utility {
   /// <summary>Finds the angle between the given two points</summary>
   public static double Angle (this Point p1, Point p2) => Math.Atan ((p1.Y - p2.Y) / (p1.X - p2.X)).ToDegrees ();

   /// <summary>Returns horizontal and vertical offset of p1 from p2 in a tuple</summary>
   public static (double DX, double DY) Diff (this Point p1, Point p2) => (p1.X - p2.X, p1.Y - p2.Y);

   /// <summary>Returns the distance between p1 and p2</summary>
   public static double Distance (this Point p1, Point p2) {
      var (dx, dy) = p1.Diff (p2);
      return Math.Sqrt (Math.Pow (dx, 2) + Math.Pow (dy, 2));
   }

   /// <summary>Finds and returns the orthogonal point for the line drawn between p1 and p2</summary>
   public static Point GetOrthoPoint (this Point p1, Point p2) {
      var ortho = new Point ();
      var angle = p1.Angle (p2);
      if (angle < 45) ortho = new (p2.X, p1.Y);
      else if (angle > 45) ortho = new (p1.X, p2.Y);
      return ortho;
   }

   /// <summary>Checks if the given reference point is near to any point within the delta distance</summary>
   public static bool HasNearestPoint (this Point refPoint, Point[] pts, double delta, out Point nearestPoint) {
      nearestPoint = pts.ToList ().Find (p => p.Distance (refPoint) <= delta);
      return !nearestPoint.IsOrigin ();
   }

   /// <summary>Returns true if the coordinates are at absolute zero</summary>
   public static bool IsOrigin (this Point pt) => pt.X is 0 && pt.Y is 0;

   /// <summary>Checks if the points are not within the viewport or not</summary>
   public static bool IsAway (this Point pt) => pt.X < 0 || pt.Y < 0;

   /// <summary>Radially moves the point to the given distance and angle</summary>
   public static Point RadialMove (this Point p, double distance, double theta) {
      theta = theta.ToRadians ();
      return new (p.X + (distance * Math.Cos (theta)), p.Y + (distance * Math.Sin (theta)));
   }

   /// <summary>Tries and parse the given string as the type Point</summary>
   public static bool TryParse (this string str, out Point p) {
      p = new ();
      var split = str.Split (',');
      if (split is null || split.Length < 2) return false;
      if (!double.TryParse (split[0], out var mX) || !double.TryParse (split[1], out var mY)) return false;
      p = new (mX, mY);
      return true;
   }

   public static bool TryParse (this string[] arr, out Point[] pts) {
      var len = arr.Length;
      List<Point> list = [];
      pts = [];
      for (int i = 0; i < len; i++) {
         if (!arr[i].TryParse (out var pt)) return false;
         list.Add (pt);
      }
      pts = [.. list];
      return true;
   }

   /// <summary>Converts the given value to radians</summary>
   public static double ToRadians (this double theta) => theta * Math.PI / 180;

   /// <summary>Converts the given value to degrees</summary>
   public static double ToDegrees (this double theta) => theta * 180 / Math.PI;

   /// <summary>Returns the quadrant of the point compared with the reference point</summary>
   public static EQuadrant Quadrant (this Point p, Point refPoint) {
      var (dx, dy) = p.Diff (refPoint);
      var quadrant = EQuadrant.I;
      if (dx < 0 && dy > 0) quadrant = EQuadrant.II;
      else if (dx < 0 && dy < 0) quadrant = EQuadrant.III;
      else if (dx > 0 && dy < 0) quadrant = EQuadrant.IV;
      return quadrant;
   }

   /// <summary>Checks if the entity is inside the bound or not</summary>
   public static bool IsInside (this Entity entity, Bound b) {
      return entity.Vertices.All (v => v.X < b.Max.X && v.X > b.Min.X && v.Y < b.Max.Y && v.Y > b.Min.Y);
   }

   /// <summary>Returns all the entities whose property selected is true</summary>
   public static Entity[] SelectedEntities (this List<Entity> entities) => entities.Where (e => e.Selected).ToArray ();

   public static string Convert (this Point pt) => $"|{pt.X},{pt.Y}";
}
#endregion