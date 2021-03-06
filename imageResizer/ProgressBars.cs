using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace imageResizer
{
    public partial class ProgressBars : Form
    {
        public ICollection FolderList { get; set; }
        public int NumFiles { get; set; }
        internal Options Options { get; set; }
        private bool _showMore = true;
        private readonly IProgress<int> TotalProgress;
        private readonly IProgress<int> Progress;
        private bool stopProcessing = false;
        private readonly Collection<string> log = new Collection<string>();

        public ProgressBars()
        {
            InitializeComponent();
            Progress = new Progress<int>(percent => progressBar1.Value = percent);
            TotalProgress = new Progress<int>(percent => progressBar2.Value = percent);
        }

        private void labelShowMore_Click(object sender, EventArgs e)
        {
            if (_showMore)
            {
                _showMore = false;
                this.labelShowMore.Text = "Show More <";
                this.Size = new Size(700, 200);
                this.label3.Visible = false;
                return;
            }
            _showMore = true;
            this.labelShowMore.Text = "Show Less ∨";
            this.Size = new Size(700, 300);
            this.label3.Visible = true;
        }

        private void ScaleImages()
        {
            foreach (var path in FolderList)
            {
                UpdateLog("Starting folder: " + path.ToString());
                if (stopProcessing) break;
                DirectoryInfo dir = new DirectoryInfo(path.ToString());
                FileInfo[] imageFiles = dir.GetFiles("*.*");
                var currentNumImages = imageFiles.Length;
                if (Options.ReducedFolder == 0 && Directory.Exists(path + "\\" + Options.FolderName + "\\")) Directory.Delete(path + "\\" + Options.FolderName + "\\", true);
                else if (Options.ReducedFolder == 1 && Directory.Exists(path + "\\" + Options.FolderName + "\\"))
                {
                    Progress.Report(progressBar1.Maximum);
                    TotalProgress.Report(progressBar2.Value + currentNumImages);
                    UpdateLog("Skipped: " + path.ToString());
                    continue;
                }
                ResizeImages(path.ToString());
            }
            UpdateLog("Done!");
            this.buttonCancel.Invoke(new MethodInvoker(() => this.buttonCancel.Text = "Finish"));
            if (stopProcessing) this.Invoke(new MethodInvoker(() => this.Close()));
        }

        private async void ProgressBars_Load(object sender, EventArgs e)
        {
            progressBar1.Value = 0;
            progressBar2.Value = 0;
            progressBar2.Maximum = NumFiles;
            await Task.Run(() => ScaleImages());
        }

        private void ResizeImages(string sourcePath)
        {
            string[] files = Directory.GetFiles(sourcePath);
            if (files.Length <= 0)
            {
                UpdateLog("Empty Directory");
                return;
            }
            string destinationPath = sourcePath + "\\" + Options.FolderName + "\\";

            System.IO.Directory.CreateDirectory(destinationPath);

            int imgIndex = 1;
            progressBar1.Invoke(new MethodInvoker(() => progressBar1.Maximum = files.Length));
            int digitCount = (int)Math.Log10(files.Length) + 1;
            for (int i = 0; i < files.Length; i++)
            {
                if (stopProcessing) break;

                UpdateLog("Loading: " + files[i]);
                var creationTime = File.GetCreationTime(sourcePath);
                string sourceBitmapPath = sourcePath + "\\" + Path.GetFileName(files[i]);
                string destinationBitmapPath = destinationPath +
                    Options.NamingConvention
                    .Replace("%n", imgIndex.ToString().PadLeft(digitCount, '0'))
                    .Replace("%f", Path.GetFileName(files[i]))
                    .Replace("%y", creationTime.Year.ToString())
                    .Replace("%m", creationTime.Month.ToString())
                    .Replace("%d", creationTime.Day.ToString()) +
                    ".jpeg";

                try
                {
                    ResizeImage(sourceBitmapPath, destinationBitmapPath);
                    imgIndex++;
                }
                catch (Exception ex)
                {
                    UpdateLog("Could not load: " + files[i]);
                    Console.WriteLine(ex.Message);
                }

                Progress.Report(i + 1);
                TotalProgress.Report(progressBar2.Value + 1);
            }
        }

        private void ResizeImage(string sourcePath, string destinationPath)
        {
            Image sourceBitmap = LoadImage(sourcePath);
            UpdateLog("Loaded image: " + sourcePath);

            var reducedSize = CalculateReducedSize(sourceBitmap.Width, sourceBitmap.Height, Options.MaxImageSize);

            var destRect = new Rectangle(0, 0, reducedSize.Width, reducedSize.Height);
            var destImage = new Bitmap(reducedSize.Width, reducedSize.Height);

            destImage.SetResolution(sourceBitmap.HorizontalResolution, sourceBitmap.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(sourceBitmap, destRect, 0, 0, sourceBitmap.Width, sourceBitmap.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            SaveJpeg(sourcePath, destImage, destinationPath);
        }

        private Image LoadImage(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ms = new MemoryStream(bytes);
            var img = Image.FromStream(ms);
            return img;
        }

        private Size CalculateReducedSize(float horizontalRes, float verticalRes, Size maxSize)
        {
            float ratio = maxSize.Width / (float)horizontalRes;
            int calculatedVerticalRes = (int)(ratio * verticalRes);

            if (calculatedVerticalRes < maxSize.Height)
            {
                return new Size(maxSize.Width, calculatedVerticalRes);
            }
            ratio = maxSize.Height / (float)verticalRes;
            int calculatedHorizontalRes = (int)(ratio * horizontalRes);
            return new Size(calculatedHorizontalRes, maxSize.Height);
        }

        private void SaveJpeg(string sourcePath, Bitmap bitmap, string destinationPath)
        {
            // Get a bitmap.
            ImageCodecInfo jpegEncoder = GetEncoder(ImageFormat.Jpeg);

            // Create an Encoder object based on the GUID for the Quality parameter category.
            System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;

            // Create an EncoderParameters object.
            // An EncoderParameters object has an array of EncoderParameter
            // objects. In this case, there is only one
            // EncoderParameter object in the array.
            EncoderParameters myEncoderParameters = new EncoderParameters(1);

            EncoderParameter myEncoderParameter = new EncoderParameter(myEncoder, Options.ImageQuality);
            myEncoderParameters.Param[0] = myEncoderParameter;
            bitmap.Save(destinationPath, jpegEncoder, myEncoderParameters);
            File.SetCreationTime(destinationPath, File.GetCreationTime(sourcePath));
            File.SetLastWriteTime(destinationPath, File.GetLastWriteTime(sourcePath));
            UpdateLog("Saved image: " + destinationPath);
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private void UpdateLog(string currentLog)
        {
            log.Add(currentLog);
            label3.Invoke(new MethodInvoker(() => label3.Text = string.Join("\n", log.Where((e, i) => i >= log.Count() - 8))));
            Console.WriteLine(currentLog);
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            if (this.buttonCancel.Text == "Finish") this.Close();
            else stopProcessing = true;
        }
    }
}
