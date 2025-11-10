using System.Drawing;
using System.Text.RegularExpressions;
using AmongusVotingScreenExtractor.Models;
using Newtonsoft.Json;
using PdfiumViewer;
using Tesseract;
using Point = System.Drawing.Point;
using Rect = Tesseract.Rect;
using Size = System.Drawing.Size;

namespace AmongusVotingScreenExtractor;

class Program
{
    private static List<MpData> _knownMembers = new();

    static void Main(string[] args)
    {
        Console.WriteLine("===========================================");
        Console.WriteLine("       Amongus Voting Screen Extractor     ");
        Console.WriteLine("===========================================\n");

        // Load parliament member data
        try
        {
            Console.WriteLine("Loading parliament member data...");
            var mpJson = File.ReadAllText("parliament_data.json");
            var parliamentData = JsonConvert.DeserializeObject<ParliamentData>(mpJson);
            _knownMembers = parliamentData?.Members ?? new List<MpData>();
            Console.WriteLine($"✓ Loaded {_knownMembers.Count} parliament members\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error loading parliament_data.json: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return;
        }

        // Display menu
        while (true)
        {
            Console.WriteLine("\nSelect an option:");
            Console.WriteLine("  1. Process all PDFs in vote_pdf folder");
            Console.WriteLine("  2. Process a single PDF file");
            Console.WriteLine("  3. Exit");
            Console.Write("\nEnter your choice (1-3): ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    ProcessBatchPdfs();
                    break;
                case "2":
                    ProcessSinglePdf();
                    break;
                case "3":
                    Console.WriteLine("\nExiting...");
                    return;
                default:
                    Console.WriteLine("\n✗ Invalid choice. Please enter 1, 2, or 3.");
                    break;
            }
        }
    }

    private static void ProcessBatchPdfs()
    {
        Console.WriteLine("\n--- Batch Processing Mode ---");

        if (!Directory.Exists("vote_pdf"))
        {
            Console.WriteLine("✗ Error: 'vote_pdf' folder not found. Creating it now...");
            Directory.CreateDirectory("vote_pdf");
            Console.WriteLine("✓ Created 'vote_pdf' folder. Please add PDF files and try again.");
            return;
        }

        var pdfFiles = Directory.GetFiles("vote_pdf", "*.pdf", SearchOption.TopDirectoryOnly);

        if (pdfFiles.Length == 0)
        {
            Console.WriteLine("✗ No PDF files found in vote_pdf folder.");
            return;
        }

        Console.WriteLine($"\nFound {pdfFiles.Length} PDF file(s) to process:");
        foreach (var pdf in pdfFiles)
        {
            Console.WriteLine($"  - {Path.GetFileName(pdf)}");
        }

        Console.WriteLine("\nProcessing files...\n");

        int successCount = 0;
        int failCount = 0;

        foreach (string pdfPath in pdfFiles)
        {
            Console.WriteLine($"\n[{successCount + failCount + 1}/{pdfFiles.Length}] Processing: {Path.GetFileName(pdfPath)}");

            if (ProcessPdfFile(pdfPath))
                successCount++;
            else
                failCount++;
        }

        Console.WriteLine("\n===========================================");
        Console.WriteLine($" Batch Processing Complete");
        Console.WriteLine($" Success: {successCount} | Failed: {failCount}");
        Console.WriteLine("===========================================");
    }

    private static void ProcessSinglePdf()
    {
        Console.WriteLine("\n--- Single File Processing Mode ---");
        Console.Write("Enter the path to the PDF file: ");
        var pdfPath = Console.ReadLine()?.Trim().Trim('"');

        if (string.IsNullOrEmpty(pdfPath))
        {
            Console.WriteLine("✗ No file path provided.");
            return;
        }

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"✗ Error: File not found: {pdfPath}");
            return;
        }

        if (!pdfPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("✗ Error: File must be a PDF.");
            return;
        }

        ProcessPdfFile(pdfPath);
    }

    private static bool ProcessPdfFile(string pdfPath)
    {
        var outputPath = Path.ChangeExtension(pdfPath, "json");

        try
        {
            Console.WriteLine("  → Starting OCR extraction...");

            using var document = PdfDocument.Load(pdfPath);
            Console.WriteLine($"  → Rendering {document.PageCount} page(s) to images...");

            for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
            {
                using var image = document.Render(pageIndex, 600, 600, PdfRenderFlags.CorrectFromDpi);
                image.Save($"{pageIndex}.jpeg");
            }

            Console.WriteLine("  → Extracting voting data with OCR...");
            var votingData = AutismMode();

            var json = JsonConvert.SerializeObject(votingData, Formatting.Indented);

            File.WriteAllText(outputPath, json);
            Console.WriteLine($"  ✓ Data extracted and saved to: {Path.GetFileName(outputPath)}");

            // Clean up temporary image files
            CleanupTempFiles();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error processing PDF: {ex.Message}");
            CleanupTempFiles();
            return false;
        }
    }

    private static void CleanupTempFiles()
    {
        try
        {
            if (File.Exists("2_id.jpeg")) File.Delete("2_id.jpeg");
            if (File.Exists("1_id.jpeg")) File.Delete("1_id.jpeg");
            if (File.Exists("1_votes.jpeg")) File.Delete("1_votes.jpeg");
            if (File.Exists("2_votes.jpeg")) File.Delete("2_votes.jpeg");
            if (File.Exists("0.jpeg")) File.Delete("0.jpeg");
            if (File.Exists("1.jpeg")) File.Delete("1.jpeg");
            if (File.Exists("2.jpeg")) File.Delete("2.jpeg");
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public static VotingData ExtractCoverPage()
    {
        var coverPage = Image.FromFile("0.jpeg");

        using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
        
        using var pix = Pix.LoadFromMemory(ImageToByteArray(coverPage));
        using var page = engine.Process(pix);

        var input = page.GetText();
        
        var data = new VotingData();

        // Timestamp (first line)
        var tsMatch = Regex.Match(input, @"Overall Voting Result (.+?)\r?\n");
        if (tsMatch.Success) data.Timestamp = tsMatch.Groups[1].Value.Trim();

        // Voting mode
        var modeMatch = Regex.Match(input, @"Voting Mode:\s*(.+)");
        if (modeMatch.Success) data.VotingMode = modeMatch.Groups[1].Value.Trim();

        // Bill number
        var billNumMatch = Regex.Match(input, @"Gaanoon number\s+(\d+/\d+)");
        // Bill title (everything after bill number until "Results")
        var billTitleMatch = Regex.Match(input, @"Gaanoon number\s+\d+/\d+\s+([\s\S]+?)Results", RegexOptions.Multiline);

        data.Bill = new Bill
        {
            Number = billNumMatch.Success ? billNumMatch.Groups[1].Value.Trim() : null,
            Title = billTitleMatch.Success ? billTitleMatch.Groups[1].Value.Trim().Replace("\n", " ").Replace("\r", " ") : null
        };

        // Summary
        var summary = new Summary
        {
            Present = GetInt(input, @"Present:\s*(\d+)"),
            EligibleToVote = GetInt(input, @"Eligible to vote:\s*(\d+)"),
            Yes = GetInt(input, @"Yes:\s*(\d+)"),
            No = GetInt(input, @"No:\s*(\d+)"),
            Abstain = GetInt(input, @"Abstain:\s*([0-9O])"), // handles "O" typo
            Voted = GetInt(input, @"Voted:\s*(\d+)"),
            NotVoted = GetInt(input, @"Not Voted:\s*(\d+)")
        };
        data.Summary = summary;
        
        coverPage.Dispose();

        return data;
    }
    
    public static VotingData AutismMode()
    {
             CropImage("1.jpeg","1_id.jpeg",0,0,500,6600);
             CropImage("1.jpeg","1_votes.jpeg",3580,0,1000,6600);
            
             CropImage("2.jpeg","2_id.jpeg",0,0,500,6600);
             CropImage("2.jpeg","2_votes.jpeg",3580,0,1000,6600);

            List<Image> indexArray = new  List<Image>();
            indexArray.Add(Image.FromFile("1_id.jpeg"));
            indexArray.Add(Image.FromFile("2_id.jpeg"));
            
            var _1votes = Image.FromFile("1_votes.jpeg");
            var _2votes = Image.FromFile("2_votes.jpeg");
            
            List<Image> voteArray = new List<Image>();
            voteArray.Add(_1votes);
            voteArray.Add(_2votes);

            string votingResults = string.Empty;
            string indexResults = string.Empty;
            
            foreach (var vote in voteArray)
            {
                // Run Tesseract OCR on the image
                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                
                // Set OCR parameters for better accuracy on clean documents
                //engine.SetVariable("tessedit_pageseg_mode", "6"); // Uniform block of text
                //engine.SetVariable("tesseract_char_whitelist", "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz .:()-'&/");
            
                using var pix = Pix.LoadFromMemory(ImageToByteArray(vote));
                using var page = engine.Process(pix);

                var text = page.GetText();
                
                votingResults += text;
                
                vote.Dispose();
            }

            foreach (var index in indexArray)
            {
                // Run Tesseract OCR on the image
                using var engine = new TesseractEngine(@"./tessdata", "digits", EngineMode.Default);
                
                // Set OCR parameters for better accuracy on clean documents
                engine.SetVariable("tessedit_pageseg_mode", "5"); // Uniform block of text
                engine.SetVariable("tesseract_char_whitelist", "0123456789");
                engine.SetVariable("load_system_dawg", "0"); // Don't load the system dictionary
                engine.SetVariable("load_freq_dawg", "0");   // Don't load the frequent words list
            
                using var pix = Pix.LoadFromMemory(ImageToByteArray(index));
                using var page = engine.Process(pix);

                var text = page.GetText();
                
                indexResults += text;
                
                index.Dispose();
            }

            indexResults = indexResults.Replace("1\n\n", "");
            votingResults= votingResults.Replace("Result\n\n", "").TrimEnd(); //have to trim end or it will add extra entity to the list
            
            List<int> indexList = indexResults
                .Split('\n', StringSplitOptions.RemoveEmptyEntries) // split lines
                .Select(line => int.Parse(line.Trim()))             // convert to int
                .ToList();
            List<string> votingList =  votingResults.Split('\n').ToList();

            votingList.RemoveAll(x => x == "");
            
            if (votingList.Count != indexList.Count)
            {
                Console.WriteLine("index out of bounds\nmismatch between voting data and index");
            }

            if (indexList.Count > _knownMembers.Count)
            {
                Console.WriteLine("index out of bounds\nmismatch between index and loaded MP data");
            }
            
            VotingData votingData = ExtractCoverPage();

            for (int i = 0; i < indexList.Count; i++)
            {
                votingData.IndividualResults.Add(new IndividualResult
                {
                    Id = i,
                    Name = _knownMembers[i].Name,
                    Party = _knownMembers[i].Party,
                    Constituency = _knownMembers[i].Constituency,
                    Result = votingList[i],
                });
            }

            if (!ValidateSummary(data: votingData))
            {
                Console.WriteLine($"Invalid summary for {votingData.Bill.Title}");
            }
            
            
            return votingData; 
    }
    


    /// <returns>true if valid</returns>
    public static bool ValidateSummary(VotingData  data)
    {
        // -1 cause the speaker is there and we have to deduct him
        var eligibleToVote = data.IndividualResults.Count - 1;
        var summary = new Summary();

        summary.Present = data.IndividualResults.Count; // everyone who has a recorded result is present
        summary.EligibleToVote = eligibleToVote;
        summary.Yes = data.IndividualResults.Count(r => r.Result.Equals("Yes", StringComparison.OrdinalIgnoreCase));
        summary.No = data.IndividualResults.Count(r => r.Result.Equals("No", StringComparison.OrdinalIgnoreCase));
        summary.Voted = summary.Yes + summary.No;
        summary.NotVoted = data.IndividualResults.Count(r => r.Result.Equals("Not Voted", StringComparison.OrdinalIgnoreCase));

        
        return summary.Present == data.Summary.Present
               && summary.EligibleToVote == data.Summary.EligibleToVote
               && summary.Yes == data.Summary.Yes
               && summary.No == data.Summary.No
               && summary.Voted == data.Summary.Voted
               && summary.NotVoted == data.Summary.NotVoted;
    }

   static private byte[] ImageToByteArray(Image image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
    
    private static int GetInt(string input, string pattern)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        if (!match.Success) return 0;

        string val = match.Groups[1].Value.Trim();
        if (val.Equals("O", StringComparison.OrdinalIgnoreCase)) return 0; // handle letter O
        return int.TryParse(val, out int result) ? result : 0;
    }
   
    public static void CropImage(string inputPath, string outputPath, int x, int y, int width, int height)
    {
        using (var original = Image.FromFile(inputPath))
        {
            Rectangle cropArea = new Rectangle(x, y, width, height);

            using (var bmpImage = new Bitmap(original))
            {
                using (var cropped = bmpImage.Clone(cropArea, bmpImage.PixelFormat))
                {
                    cropped.Save(outputPath);
                }
            }
        }
    }
}