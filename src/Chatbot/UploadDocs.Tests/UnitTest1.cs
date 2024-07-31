using NUnit.Framework;
using UploadDocsTests;
using Moq;
using System.IO;
using System;


namespace UploadDocsTests.Tests
{
    [TestFixture]
    public class UploadDocsTests
    {
        private Mock<IFileProcessor> _fileProcessorMock;
        private UploadService _uploadService;

        [SetUp]
        public void Setup()
        {
            // Mock the file processor to simulate file operations
            _fileProcessorMock = new Mock<IFileProcessor>();
            _uploadService = new UploadService(_fileProcessorMock.Object);
        }

        [Test]
        public void UploadMarkdownFiles_ShouldCallProcessFileForEachFile()
        {
            // Arrange
            var files = new string[] { "file1.md", "file2.md", "file3.md" };
            _fileProcessorMock.Setup(f => f.GetFiles(It.IsAny<string>())).Returns(files);

            // Act
            _uploadService.UploadMarkdownFiles("path/to/markdown/files");

            // Assert
            foreach (var file in files)
            {
                _fileProcessorMock.Verify(f => f.ProcessFile(file), Times.Once);
            }
        }

        [Test]
        public void UploadMarkdownFiles_ShouldHandleNoFilesFound()
        {
            // Arrange
            _fileProcessorMock.Setup(f => f.GetFiles(It.IsAny<string>())).Returns(Array.Empty<string>());

            // Act & Assert
            Assert.DoesNotThrow(() => _uploadService.UploadMarkdownFiles("path/to/markdown/files"));
        }
    }
}

// Your application code should include interfaces and classes that can be tested.
// For example:
public interface IFileProcessor
{
    string[] GetFiles(string directory);
    void ProcessFile(string filePath);
}

public class UploadService
{
    private readonly IFileProcessor _fileProcessor;

    public UploadService(IFileProcessor fileProcessor)
    {
        _fileProcessor = fileProcessor;
    }

    public void UploadMarkdownFiles(string directory)
    {
        var files = _fileProcessor.GetFiles(directory);
        foreach (var file in files)
        {
            _fileProcessor.ProcessFile(file);
        }
    }
}