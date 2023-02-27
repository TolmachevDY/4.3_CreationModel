using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.ApplicationServices;

namespace CreationModel
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class CreationModel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var walls = CreateWalls(doc);
            AddDoor(doc, walls[0]);
            AddWindows(doc, walls);
            AddRoof(doc, walls[3]);

            return Result.Succeeded;
        }
        //метод создания крыши
        private void AddRoof(Document doc, Wall wall)
        {
            Level level2 = GetLevels(doc)
                .Where(x => x.Name.Equals("Уровень 2"))
                .FirstOrDefault();
            //получаем тип крыши
            RoofType roofType = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .OfType<RoofType>()
                .Where(x => x.Name.Equals("Типовой - 400мм"))
                .Where(x => x.FamilyName.Equals("Базовая крыша"))
                .FirstOrDefault();
            //задаем смещение контура крыши
            double wallWidth = wall.Width;
            double dt = wallWidth / 2;
            double dz = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble();
            //находим точки контура выдавливания
            LocationCurve locationCurve = wall.Location as LocationCurve;
            XYZ point1 = locationCurve.Curve.GetEndPoint(0) + new XYZ(-dt, dt, dz);
            XYZ point2 = locationCurve.Curve.GetEndPoint(1) + new XYZ(-dt, -dt, dz);
            XYZ delta = point2 - point1;
            XYZ midPoint = point1 + delta / 2 + new XYZ(0, 0, UnitUtils.ConvertToInternalUnits(1000, UnitTypeId.Millimeters));
            //создаем линии контура выдавливания
            CurveArray footprint = new CurveArray();
            footprint.Append(Line.CreateBound(point1, midPoint));
            footprint.Append(Line.CreateBound(midPoint, point2));

            Transaction transaction = new Transaction(doc, "Построение крыши");
            transaction.Start();
            //создаем опорную плоскость
            ReferencePlane plane = doc.Create.NewReferencePlane(point1, point1 + new XYZ(0, 0, -1), point2 - point1, doc.ActiveView);
            //создаем крышу
            doc.Create.NewExtrusionRoof(footprint, plane, level2, roofType, 0, UnitUtils.ConvertToInternalUnits(10000, UnitTypeId.Millimeters) + wallWidth);
            transaction.Commit();
        }

        //метод для получения списка уровней
        public List<Level> GetLevels(Document doc)
        {
            var listLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .ToList();
            return listLevel;
        }

        //метод для получения точек построения стен
        public List<XYZ> GetPoints(double x, double y)
        {
            double width = UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(y, UnitTypeId.Millimeters);
            double dx = width / 2;
            double dy = depth / 2;

            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dx, -dy, 0));
            points.Add(new XYZ(dx, -dy, 0));
            points.Add(new XYZ(dx, dy, 0));
            points.Add(new XYZ(-dx, dy, 0));
            points.Add(new XYZ(-dx, -dy, 0));

            return points;
        }

        //метод для построения стен
        public List<Wall> CreateWalls(Document doc)
        {
            //получаем уровнь 1
            Level level1 = GetLevels(doc)
                .Where(x => x.Name.Equals("Уровень 1"))
                .FirstOrDefault();
            //получаем уровнь 2
            Level level2 = GetLevels(doc)
                .Where(x => x.Name.Equals("Уровень 2"))
                .FirstOrDefault();
            Transaction transaction = new Transaction(doc, "Построение стен");
            transaction.Start();
            List<Wall> walls = new List<Wall>();//пустой список стен, куда добавляем создаваемые стены 
            for (int i = 0; i < 4; i++)
            {
                //получаем точки для построения
                List<XYZ> points = GetPoints(10000, 5000);
                Line line = Line.CreateBound(points[i], points[i + 1]);//создаем линию, по которой будет строится стена
                Wall wall = Wall.Create(doc, line, level1.Id, false);//создаем стену
                wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE).Set(level2.Id);//записываем в параметр стены значение зависимости сверху
                walls.Add(wall);//добавляем стену в список
            }
            transaction.Commit();
            return walls;
        }
        //метод для создания двери
        public void AddDoor(Document doc, Wall wall)
        {
            Level level1 = GetLevels(doc)
            .Where(x => x.Name.Equals("Уровень 1"))
            .FirstOrDefault();

            Transaction transaction = new Transaction(doc, "Добавление двери");
            transaction.Start();

            FamilySymbol doorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0915 x 2032 мм"))
                .Where(x => x.FamilyName.Equals("Одиночные-Щитовые"))
                .FirstOrDefault();
            //получение точки вставки двери
            LocationCurve hostCurve = wall.Location as LocationCurve;
            XYZ point1 = hostCurve.Curve.GetEndPoint(0);
            XYZ point2 = hostCurve.Curve.GetEndPoint(1);
            XYZ point = (point1 + point2) / 2;
            //активируем тип
            if (!doorType.IsActive)
                doorType.Activate();
            //создаем дверь
            doc.Create.NewFamilyInstance(point, doorType, wall, level1, StructuralType.NonStructural);

            transaction.Commit();
        }
        //метод для создания окон
        public void AddWindows(Document doc, List<Wall> walls)
        {
            Level level1 = GetLevels(doc)
            .Where(x => x.Name.Equals("Уровень 1"))
            .FirstOrDefault();

            Transaction transaction = new Transaction(doc, "Добавление окон");
            transaction.Start();

            FamilySymbol windowType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0406 x 0610 мм"))
                .Where(x => x.FamilyName.Equals("Фиксированные"))
                .FirstOrDefault();
            foreach (var wall in walls)
            {
                if (wall == walls[0])
                    continue;
                //получение точки вставки окна
                LocationCurve hostCurve = wall.Location as LocationCurve;
                XYZ point1 = hostCurve.Curve.GetEndPoint(0);
                XYZ point2 = hostCurve.Curve.GetEndPoint(1);
                XYZ point = (point1 + point2) / 2;
                //активируем тип
                if (!windowType.IsActive)
                    windowType.Activate();
                //создаем окно
                doc.Create.NewFamilyInstance(point, windowType, wall, level1, StructuralType.NonStructural);
            }

            transaction.Commit();
        }

    }
}
