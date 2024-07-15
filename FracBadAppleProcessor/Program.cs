using Microsoft.VisualBasic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors;
using SixLabors.ImageSharp.Processing.Processors.Binarization;
using System.Data;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
namespace FracBadAppleProfessor 
{
    //我因为预先知道我文件都塞在哪个位置，所以路径相关的直接就代码里硬编码了((((
    internal class Program
    {
        static void BlackWhite()
        {
            string path = "D:\\baTest\\";
            Parallel.ForEach(Enumerable.Range(1, 13137), n =>
            {
                using Image image = Image.Load(path + $"orig\\orig_{n:00000}.png");
                image.Mutate(x => x.Crop(new Rectangle(480, 0, 2880, 2160)));//对画面进行剪切，我下载到的是16:9的，但是原本画面是4:3的，剪切之后才能计算正确的SDF

                image.Mutate(x => x.ApplyProcessor(new BinaryThresholdProcessor(0.33f)));//阈值处理，亮度高于0.33f的会变成白色，否则是黑色，同样是为SDF准备
                ImageExtensions.SaveAsPng(image, path + $"cut\\blackWhite_{n:00000}.png");//暂时存出一下
            });//一共导出了13137帧，用ffmpeg导出的
        }
        /// <summary>
        /// 程序的入口
        /// </summary>
        /// <param name="args">该程序打开的文件的路径，因为可以有多个文件所以是数组，我记得一次最多选中896张还是多少，反正是16的倍数</param>
        static void Main(string[] args)
        {
            //D:\\baTest是最外层文件夹
            //其下\\assets是一些方便调用的资源文件，曼德勃特的生成可以看这个 https://www.shadertoy.com/view/43f3RM
            //其下\\orig是原视频直接导出的图片集
            //其下\\cut是经过裁切和黑白处理得到的图片集
            //其下\\final是最终每一帧的图片
            //D:\\baTest\\cut\\result存了距离场图
            //D:\\baTest\\cut\\result\\result存了分形风格图
            //将分形风格图全部复制出
            //再复制出没处理的几十帧纯黑图(用程序复制改名的
            //这些丢到\\final
            //最后用ffmpeg把图片合成为视频就完事了


            //BlackWhite();//先进行一次对原图的预处理

            //args = Directory.GetFiles("D:\\baTest\\cut\\result");
            int m = args.Length;
            if (m > 0)
            {
                int counter = 0;
                Parallel.ForEach(args, str =>
                {
                    string path = Path.GetDirectoryName(str);
                    using Image image = Image.Load(str);
                    //image.Mutate(x => x.DistanceField()); //进行距离场的计算，必须要强调的是有几张图是纯黑的，不能丢进去计算
                    image.Mutate(x => x.FractalRender(0));  //基于距离场进行分形映射上色
                    if (!Directory.Exists(path + "\\result")) Directory.CreateDirectory(path + "\\result");//如果路径下不存在result文件夹就新建一个吧
                    image.SaveAsPng(path + "\\result\\" + Path.GetFileName(str).Replace("dist", "frac"));
                    counter++;
                    Console.WriteLine($"{counter * 100f / m}%");//输出这一组的进度
                    //Console.WriteLine(path);
                });
            }
            //别问我为什么不交给显卡来进行计算，问就是不会不借助框架用GLSL(
            Console.WriteLine("输出完毕");
            Console.ReadLine();

            //Parallel.ForEach(Enumerable.Range(1, 82).Union(Enumerable.Range(9134, 10)).Union(Enumerable.Range(9186, 10)).Union(Enumerable.Range(13028, 110)), n => 
            //{
            //    File.Copy($"D:\\baTest\\cut\\blackWhite_{n:00000}.png", $"D:\\baTest\\final\\frac_{n:00000}.png");
            //});//这里是把纯黑的那几张改个名直接复制到最终的输出文件夹
            return;
        }


    }

    //通过拓展函数方便调用下面两个处理器，仅此而已
    public static class LogSpiralProcessorExtension
    {
        public static IImageProcessingContext FractalRender(this IImageProcessingContext source, float offsetAngle = 0f)
    => source.ApplyProcessor(new FractalProcessor(offsetAngle));
        public static IImageProcessingContext DistanceField(this IImageProcessingContext source)
    => source.ApplyProcessor(new SDFProcessor());
        //public static IImageProcessingContext Cut(this IImageProcessingContext source) => source.ApplyProcessor()
    }


    //基于距离场贴图的分形渲染实现
    public class FractalProcessor : IImageProcessor
    {
        public float offsetAngle;
        public FractalProcessor(float offsetAng = 0f)
        {
            offsetAngle = offsetAng;
        }
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle) where TPixel : unmanaged, IPixel<TPixel>
        {
            return new FractalProcessor<TPixel>(configuration, source, sourceRectangle, offsetAngle);
        }
    }
    public class FractalProcessor<TPixel> : ImageProcessor<TPixel> where TPixel : unmanaged, IPixel<TPixel>
    {
        //曼德勃特模式
        static Image<Rgba32> fracImage = (Image<Rgba32>)Image.Load("D:\\baTest\\assets\\WallPaper_FractalMandelbort.png");
        //蔷薇模式
        //static Image<Rgba32> fracImage = (Image<Rgba32>)Image.Load("D:\\baTest\\assets\\WallPaper_FractalRose.png");

        /// <summary>
        /// 偏转角，我后来渲染没用上其实
        /// </summary>
        public float offsetAngle;
        private readonly struct RowOperation : IRowOperation<Rgb24>
        {
            private readonly Buffer2D<TPixel> source;
            private readonly int startX;
            private readonly Configuration configuration;
            private readonly float offsetAngle;

            public int GetRequiredBufferLength(Rectangle bounds) => bounds.Width;
            public RowOperation(int start, Buffer2D<TPixel> buffer, Configuration config, float offsetAng = 0f)
            {
                startX = start;
                source = buffer;
                configuration = config;
                offsetAngle = offsetAng;
            }
            public void Invoke(int y, Span<Rgb24> span)
            {
                Span<TPixel> rowSpan = this.source.DangerousGetRowSpan(y).Slice(this.startX, span.Length);
                PixelOperations<TPixel>.Instance.ToRgb24(this.configuration, rowSpan, span);
                for (int x = 0; x < rowSpan.Length; x++)
                {
                    ref TPixel color = ref rowSpan[x];
                    Rgba32 rgba32 = default;
                    color.ToRgba32(ref rgba32);//读取到距离场图上的一个像素
                    int distSqr = rgba32.B + 255 * (rgba32.G + 255 * rgba32.R);//把像素信息转距离信息
                    Complex orig = new Complex(-32 / 9.0, -2);//缩放中心
                    Complex vec = Complex.Exp(Complex.ImaginaryOne * (Math.Atan2(y - source.Height * .5, x - source.Width * .5) * 4 + offsetAngle)) * 0.5;
                    vec -= vec * vec;
                    vec *= 0.95 + Math.Clamp(Math.Sqrt(distSqr) / 8000.0, 0, 1000.0) * 64.0;//这里如果不进行缩放，得到的点是曼德勃特的内边界
                    //↑具体可以看这两个
                    // https://www.shadertoy.com/view/MltXz2
                    // https://iquilezles.org/articles/mset1bulb


                    //注释掉的这一段代码是使用蔷薇图来取色的，但是效果没曼德勃特好就还是用曼德勃特了
                    /*
                    Complex vec = Complex.Exp(new Complex(0, Math.Atan2(y - source.Height * .5, x - source.Width * .5) * 4)) * 0.5 + 0.5;
                    vec = 2 * vec - vec * vec + 0.2;
                    vec *= 0.75;
                    vec -= 0.72;
                    double scale = (1.1 + Math.Clamp(Math.Sqrt(distSqr) / 450.0, 0, 1000.0) * 64.0);
                    vec *= scale;
                    vec += 0.72;
                    */
                    vec -= orig;
                    vec *= 270;//整体乘上270，使得接下来能正确地在那张1920x1080的曼德勃特图上取色
                    try
                    {
                        var pix = fracImage[(int)Math.Clamp(vec.Real, 0, 1919), (int)Math.Clamp(vec.Imaginary, 0, 1079)];//通过点的坐标获取颜色然后进行上色
                        color = new Color(new Rgb24(pix.R, pix.G, pix.B)).ToPixel<TPixel>();

                    }
                    catch
                    {
                        Console.WriteLine(fracImage.GetType());
                    }
                }
            }
        }
        public FractalProcessor(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle, float offsetAng = 0f) : base(configuration, source, sourceRectangle)
        {
            offsetAngle = offsetAng;
        }

        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            Rectangle sourceRectangle = this.SourceRectangle;
            Configuration configuration = this.Configuration;

            //进行按列还是按行的并行运算
            var interest = Rectangle.Intersect(sourceRectangle, source.Bounds());
            var operation = new RowOperation(
                interest.X,
                source.PixelBuffer,
                configuration, offsetAngle);

            ParallelRowIterator.IterateRows<RowOperation, Rgb24>(
                configuration,
                interest,
                in operation);
        }
    }


    //距离场计算实现
    public class SDFProcessor : IImageProcessor
    {
        //为了解除泛型耦合(?)而生的非泛型类
        public IImageProcessor<TPixel> CreatePixelSpecificProcessor<TPixel>(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle) where TPixel : unmanaged, IPixel<TPixel>
        {
            return new SDFProcessor<TPixel>(configuration, source, sourceRectangle);
        }
    }
    public class SDFProcessor<TPixel> : ImageProcessor<TPixel> where TPixel : unmanaged, IPixel<TPixel>
    {
        const int scale = 4096;
        //数据图dataSet的规格，有   64       256         1024         4096     的，
        //最远的格点分别是       (62,62)  (254,254)   (1022,1022)  (4094,4094)
        //也就是说长宽大于4000的图用我的迫真算法就搞不了乐
        //为什么不导出更高规格的数据图呢?首先4096那个已经31M了，其次是我试过了，太大的图片会导出不了
        //不过也应该不会有人打算处理那么大的图片吧不会吧

        static Image<Rgba32> dataSet = (Image<Rgba32>)Image.Load($"D:\\baTest\\assets\\dataSet{scale}.png");
        //↑这里是对扫描的像素位置进行打表，两行一列为一个点的坐标，从左往右从上到下读取
        //按照离原点距离的第一象限右下半部格点排序依次是(部分示例)
        //下标 坐标偏移 到当前点距离的平方
        //0    (0,0)    0
        //1    (1,0)    1
        //2    (1,1)    2
        //3    (2,0)    4
        //4    (2,1)    5
        //5    (2,2)    8
        //6    (3,0)    9
        //7    (3,1)    10
        //8    (3,2)    13
        //9    (4,0)    16
        //10   (3,3)    18
        //照着这个顺序依次检测就能找到离某点最近的目标点，从而得到这个点处的距离值
        //后面的点在这个点的基础上计算就能大大降低计算量(不然大抵是O(n^3)罢((

        
        public SDFProcessor(Configuration configuration, Image<TPixel> source, Rectangle sourceRectangle) : base(configuration, source, sourceRectangle)
        {

        }

        /// <summary>
        /// 用颜色来存距离信息，不用alpha通道是因为会稍微有点问题，而且目前没必要
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        static Color IntToColor(int p)
        {
            return Color.FromRgba((byte)(p >> 16), (byte)(p >> 8 % 256), (byte)p, 255);
        }


        //下面三个是淘汰的，目前使用新的ESSEDT
        /// <summary>
        /// 逐个像素计算SDF，利用上一个像素的迭代信息进行估值，所以不能完全并行(也就是我目前不会交给GPU计算
        /// </summary>
        /// <param name="source"></param>
        void OrigSDF(ImageFrame<TPixel> source)
        {
            int width = source.Width;
            int height = source.Height;
            Span<TPixel> result = new Span<TPixel>(new TPixel[width * height]);
            var buffer = source.PixelBuffer;//要处理的图片
            int lastRowCounter = 0;//上一行第一个，用来切换行的时候赋上正确的lastCounter
            for (int i = 0; i < width; i++)
            {
                int lastCounter = lastRowCounter;//上一个像素的查找次数，用来估计下一次开始查找的锚点
                for (int j = 0; j < height; j++)
                {
                    bool flag = true;
                    int counter = 0;//计数器，表示对当前像素查找的次数
                    if (lastCounter > 3)
                        counter = Math.Max((int)(lastCounter + 1 - Math.Sqrt(lastCounter) * 2) - 10, 0);//某种概念上的放缩，基于三角不等式和估阶
                    Vector2 unit = default;
                    float dist = 0;
                    while (flag)
                    {
                        int x = counter % scale;
                        int y = counter / scale * 2;
                        Rgba32 xData = dataSet[x, y];//从数据图中获得xy偏移量
                        Rgba32 yData = dataSet[x, y + 1];
                        unit = new Vector2((xData.R * 256 + xData.G) * 256 + xData.B, (yData.R * 256 + yData.G) * 256 + yData.B);//生成偏移向量
                        for (int n = 0; n < 8; n++)
                        {
                            Vector2 _unit = unit;
                            //以下三行对应三个对称操作，由一个偏移向量生成等模长的三个
                            if (n > 3) _unit = new Vector2(_unit.Y, _unit.X);
                            _unit *= new Vector2(n / 2 % 2 * 2 - 1, n % 2 * 2 - 1);
                            _unit += new Vector2(i, j);

                            //查询格点，如果是白色像素就停止(只有两个状态，所以我直接x>0了
                            if (buffer[(int)Math.Clamp(_unit.X, 0, width - 1), (int)Math.Clamp(_unit.Y, 0, height - 1)].ToVector4().X > 0)
                            {
                                flag = false;//停止当前像素的查找
                                dist = unit.LengthSquared();//记录该像素到最近白色像素的距离的平方
                                unit = _unit;
                                lastCounter = counter;//记录该次查找次数
                                if (j == 0)
                                    lastRowCounter = counter;//如果是每行第一个，给这个赋上新值
                                break;
                            }
                        }
                        counter++;//查询次数自增
                    }
                    ref TPixel color = ref result[i + j * width];
                    color = IntToColor((int)dist).ToPixel<TPixel>();//赋上当前像素的结果
                }
                if (i % 20 == 0)
                    Console.WriteLine($"{i / (float)width * 100:0.00}%");
            }
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                {
                    buffer[i, j] = result[i + j * width];
                }
        }
        /// <summary>
        /// 本来打算整并行的SDF，但是好像用不了的样子
        /// </summary>
        /// <param name="source"></param>
        void ParallelSDF(ImageFrame<TPixel> source)
        {
            int width = source.Width;
            int height = source.Height;
            Span<TPixel> result = new Span<TPixel>(new TPixel[width * height]);
            var buffer = source.PixelBuffer;//要处理的图片
            int lastRowCounter = 0;//上一行第一个，用来切换行的时候赋上正确的lastCounter
            for (int i = 0; i < width; i++)
            {
                int lastCounter = lastRowCounter;//上一个像素的查找次数，用来估计下一次开始查找的锚点
                for (int j = 0; j < height; j++)
                {
                    bool flag = true;
                    int counter = 0;//计数器，表示对当前像素查找的次数
                    if (lastCounter > 3)
                        counter = Math.Max((int)(lastCounter + 1 - Math.Sqrt(lastCounter) * 2) - 10, 0);//某种概念上的放缩，基于三角不等式和估阶
                    Vector2 unit = default;
                    float dist = 0;
                    while (flag)
                    {
                        int x = counter % scale;
                        int y = counter / scale * 2;
                        Rgba32 xData = dataSet[x, y];//从数据图中获得xy偏移量
                        Rgba32 yData = dataSet[x, y + 1];
                        unit = new Vector2((xData.R * 256 + xData.G) * 256 + xData.B, (yData.R * 256 + yData.G) * 256 + yData.B);//生成偏移向量
                        for (int n = 0; n < 8; n++)
                        {
                            Vector2 _unit = unit;
                            //以下三行对应三个对称操作，由一个偏移向量生成等模长的三个
                            if (n > 3) _unit = new Vector2(_unit.Y, _unit.X);
                            _unit *= new Vector2(n / 2 % 2 * 2 - 1, n % 2 * 2 - 1);
                            _unit += new Vector2(i, j);

                            //查询格点，如果是白色像素就停止(只有两个状态，所以我直接x>0了
                            if (buffer[(int)Math.Clamp(_unit.X, 0, width - 1), (int)Math.Clamp(_unit.Y, 0, height - 1)].ToVector4().X > 0)
                            {
                                flag = false;//停止当前像素的查找
                                dist = unit.LengthSquared();//记录该像素到最近白色像素的距离的平方
                                unit = _unit;
                                lastCounter = counter;//记录该次查找次数
                                if (j == 0)
                                    lastRowCounter = counter;//如果是每行第一个，给这个赋上新值
                                break;
                            }
                        }
                        counter++;//查询次数自增
                    }
                    ref TPixel color = ref result[i + j * width];
                    color = IntToColor((int)dist).ToPixel<TPixel>();//赋上当前像素的结果
                }
                if (i % 20 == 0)
                    Console.WriteLine($"{i / (float)width * 100:0.00}%");
            }
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                {
                    buffer[i, j] = result[i + j * width];
                }
        }
        void RowOpera(ImageFrame<TPixel> source)
        {
            Rectangle sourceRectangle = this.SourceRectangle;
            Configuration configuration = this.Configuration;


            var interest = Rectangle.Intersect(sourceRectangle, source.Bounds());
            var operation = new RowOperation(
                interest.X,
                source.PixelBuffer,
                configuration);

            ParallelRowIterator.IterateRows<RowOperation, Rgb24>(
                configuration,
                interest,
                in operation);
        }
        private readonly struct RowOperation : IRowOperation<Rgb24>
        {
            private readonly Buffer2D<TPixel> source;
            private readonly int startX;
            private readonly Configuration configuration;
            public int GetRequiredBufferLength(Rectangle bounds) => bounds.Width;
            public RowOperation(int start, Buffer2D<TPixel> buffer, Configuration config)
            {
                startX = start;
                source = buffer;
                configuration = config;
            }
            //readonly List<int> lastCounters = new List<int>();
            public void Invoke(int y, Span<Rgb24> span)
            {
                Span<TPixel> rowSpan = this.source.DangerousGetRowSpan(y).Slice(this.startX, span.Length);
                Span<TPixel> pxBuffer = new Span<TPixel>(new TPixel[span.Length]);
                PixelOperations<TPixel>.Instance.ToRgb24(this.configuration, rowSpan, span);
                int lastCounter = 0;
                for (int x = 0; x < rowSpan.Length; x++)
                {
                    ref TPixel color = ref pxBuffer[x];
                    bool flag = true;
                    int counter = 0;
                    if (lastCounter > 3)
                        counter = Math.Max((int)(lastCounter + 1 - Math.Sqrt(lastCounter) * 2) - 10, 0);
                    Vector2 unit = default;
                    float dist = 0;
                    while (flag)
                    {
                        int cx = counter % scale;
                        int cy = counter / scale * 2;
                        Rgba32 xData = default;
                        dataSet[cx, cy].ToRgba32(ref xData);
                        Rgba32 yData = default;
                        dataSet[cx, cy + 1].ToRgba32(ref yData);
                        unit = new Vector2((xData.R * 256 + xData.G) * 256 + xData.B, (yData.R * 256 + yData.G) * 256 + yData.B);
                        for (int n = 0; n < 8; n++)
                        {
                            Vector2 _unit = unit;
                            if (n > 3) _unit = new Vector2(_unit.Y, _unit.X);
                            _unit *= new Vector2(n / 2 % 2 * 2 - 1, n % 2 * 2 - 1);
                            _unit += new Vector2(x, y);
                            if (source[(int)Math.Clamp(_unit.X, 0, source.Width - 1), (int)Math.Clamp(_unit.Y, 0, source.Height - 1)].ToScaledVector4().X > 0)
                            {
                                flag = false;
                                dist = unit.LengthSquared();
                                unit = _unit;
                                lastCounter = counter;
                                break;
                            }
                        }
                        counter++;
                    }
                    color = IntToColor((int)dist).ToPixel<TPixel>();
                }
                for (int x = 0; x < rowSpan.Length; x++)
                {
                    ref TPixel color = ref rowSpan[x];
                    color = pxBuffer[x];
                }
            }
        }
        //bool ESSEDT(ImageFrame<TPixel> source) 
        //{
        //    int width = source.Width;
        //    int height = source.Height;
        //    Span<TPixel> result = new Span<TPixel>(new TPixel[width * height]);
        //    var buffer = source.PixelBuffer;//要处理的图片


        //    return false;
        //}

        /// <summary>
        /// 计算图片的SDF，不是最准的但是够用，更何况没人会计较那一两个像素吧不会吧
        /// </summary>
        /// <param name="source"></param>
        void ESSEDT(ImageFrame<TPixel> source)
        {
            #region 初始化
            int width = source.Width;
            int height = source.Height;
            Vector2[,] deltaS = new Vector2[width, height];
            //Span<TPixel> result = new Span<TPixel>(new TPixel[width * height]);
            var buffer = source.PixelBuffer;//要处理的图片
            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                {
                    deltaS[i, j] = buffer[i, j].ToVector4().X > 0.1f ? default : new Point(width, height);
                }
            #endregion


            #region 第一个像素(左上)
            {
                bool flag = true;
                int counter = 0;//计数器，表示对当前像素查找的次数
                Vector2 unit = default;
                float dist = 0;
                while (flag)
                {
                    int x = counter % scale;
                    int y = counter / scale * 2;
                    Rgba32 xData = dataSet[x, y];//从数据图中获得xy偏移量
                    Rgba32 yData = dataSet[x, y + 1];
                    unit = new Vector2((xData.R * 256 + xData.G) * 256 + xData.B, (yData.R * 256 + yData.G) * 256 + yData.B);//生成偏移向量
                    for (int n = 0; n < 2; n++)
                    {
                        Vector2 _unit = unit;
                        //以下三行对应三个对称操作，由一个偏移向量生成等模长的三个
                        if (n > 0) _unit = new Vector2(_unit.Y, _unit.X);

                        //查询格点，如果是白色像素就停止(只有两个状态，所以我直接x>0了
                        if (buffer[(int)Math.Clamp(_unit.X, 0, width - 1), (int)Math.Clamp(_unit.Y, 0, height - 1)].ToVector4().X > 0)
                        {
                            flag = false;//停止当前像素的查找
                            dist = unit.LengthSquared();//记录该像素到最近白色像素的距离的平方
                            unit = _unit;
                            break;
                        }
                    }
                    counter++;//查询次数自增
                }
                deltaS[0, 0] = unit;
            }

            #endregion
            #region 上到下扫描
            for (int j = 0; j < height; j++)
                for (int i = 0; i < width; i++)
                {
                    if (deltaS[i, j] == default) continue;
                    ref Vector2 cur = ref deltaS[i, j];
                    if (i != 0)
                    {
                        Vector2 tar = deltaS[i - 1, j] + new Vector2(-1, 0);
                        if (tar.LengthSquared() < cur.LengthSquared())
                        {
                            cur = tar;
                        }

                    }
                    if (j != 0)
                    {
                        Vector2 tar = deltaS[i, j - 1] + new Vector2(0, -1);
                        if (tar.LengthSquared() < cur.LengthSquared())
                        {
                            cur = tar;
                        }
                        if (i != 0)
                        {
                            tar = deltaS[i - 1, j - 1] + new Vector2(-1, -1);
                            if (tar.LengthSquared() < cur.LengthSquared())
                            {
                                cur = tar;
                            }
                        }
                        if (i != width - 1)
                        {
                            tar = deltaS[i + 1, j - 1] + new Vector2(1, -1);
                            if (tar.LengthSquared() < cur.LengthSquared())
                            {
                                cur = tar;
                            }
                        }
                    }
                }
            #endregion
            #region 第一个像素(右下)
            {
                bool flag = true;
                int counter = 0;//计数器，表示对当前像素查找的次数
                Vector2 unit = default;
                float dist = 0;
                while (flag)
                {
                    int x = counter % scale;
                    int y = counter / scale * 2;
                    Rgba32 xData = dataSet[x, y];//从数据图中获得xy偏移量
                    Rgba32 yData = dataSet[x, y + 1];
                    unit = new Vector2((xData.R * 256 + xData.G) * 256 + xData.B, (yData.R * 256 + yData.G) * 256 + yData.B);//生成偏移向量
                    for (int n = 0; n < 2; n++)
                    {
                        Vector2 _unit = unit;
                        //以下三行对应三个对称操作，由一个偏移向量生成等模长的三个
                        if (n > 0) _unit = new Vector2(_unit.Y, _unit.X);
                        _unit *= -1;
                        _unit += new Vector2(width - 1, height - 1);

                        //查询格点，如果是白色像素就停止(只有两个状态，所以我直接x>0了
                        if (buffer[(int)Math.Clamp(_unit.X, 0, width - 1), (int)Math.Clamp(_unit.Y, 0, height - 1)].ToVector4().X > 0)
                        {
                            flag = false;//停止当前像素的查找
                            dist = unit.LengthSquared();//记录该像素到最近白色像素的距离的平方
                            unit = _unit;
                            break;
                        }
                    }
                    counter++;//查询次数自增
                }
                Vector2 tar = unit - new Vector2(width - 1, height - 1);
                if (tar.LengthSquared() < deltaS[width - 1, height - 1].LengthSquared())
                    deltaS[width - 1, height - 1] = tar;
            }
            #endregion
            #region 下到上扫描
            for (int j = height - 1; j >= 0; j--)
                for (int i = width - 1; i >= 0; i--)
                {
                    if (deltaS[i, j] == default) continue;
                    ref Vector2 cur = ref deltaS[i, j];
                    if (i != width - 1)
                    {
                        Vector2 tar = deltaS[i + 1, j] + new Vector2(1, 0);
                        if (tar.LengthSquared() < cur.LengthSquared())
                        {
                            cur = tar;
                        }

                    }
                    if (j != height - 1)
                    {
                        Vector2 tar = deltaS[i, j + 1] + new Vector2(0, 1);
                        if (tar.LengthSquared() < cur.LengthSquared())
                        {
                            cur = tar;
                        }
                        if (i != 0)
                        {
                            tar = deltaS[i - 1, j + 1] + new Vector2(-1, 1);
                            if (tar.LengthSquared() < cur.LengthSquared())
                            {
                                cur = tar;
                            }
                        }
                        if (i != width - 1)
                        {
                            tar = deltaS[i + 1, j + 1] + new Vector2(1, 1);
                            if (tar.LengthSquared() < cur.LengthSquared())
                            {
                                cur = tar;
                            }
                        }
                    }
                }
            #endregion

            for (int i = 0; i < width; i++)
                for (int j = 0; j < height; j++)
                {
                    buffer[i, j] = IntToColor((int)deltaS[i, j].LengthSquared()).ToPixel<TPixel>();//用像素来记录距离信息
                }
        }


        /// <summary>
        /// 将图片进行处理，这里是处理成距离场图
        /// </summary>
        /// <param name="source"></param>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            //while (!ESSEDT(source));
            //OrigSDF(source);


            ESSEDT(source);
        }
    }
}
