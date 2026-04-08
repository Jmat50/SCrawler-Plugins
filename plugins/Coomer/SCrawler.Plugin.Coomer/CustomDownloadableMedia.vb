Imports System.IO
Imports SCrawler.Plugin

Namespace CoomerPlugin
    Public Class CustomDownloadableMedia
        Implements IDownloadableMedia

        Public Event CheckedChange As EventHandler Implements IDownloadableMedia.CheckedChange
        Public Event ThumbnailChanged As EventHandler Implements IDownloadableMedia.ThumbnailChanged
        Public Event StateChanged As EventHandler Implements IDownloadableMedia.StateChanged

        Private Shared ReadOnly SiteIconImage As Drawing.Image = Drawing.SystemIcons.Application.ToBitmap()

        Private _checked As Boolean
        Private _thumbnailUrl As String
        Private _thumbnailFile As String
        Private _state As UserMediaStates = UserMediaStates.Unknown

        Public Sub New(ByVal url As String, ByVal outputFile As String)
            Me.URL = url
            Me.URL_BASE = url
            Me.File = outputFile
            Me.ContentType = UserMediaTypes.VideoPre
            Me.Title = Path.GetFileNameWithoutExtension(outputFile)
            If String.IsNullOrWhiteSpace(Me.Title) Then Me.Title = "Custom Media"
        End Sub

        Public ReadOnly Property SiteIcon As Drawing.Image Implements IDownloadableMedia.SiteIcon
            Get
                Return SiteIconImage
            End Get
        End Property

        Public ReadOnly Property Site As String Implements IDownloadableMedia.Site
            Get
                Return SiteDisplayName
            End Get
        End Property

        Public ReadOnly Property SiteKey As String Implements IDownloadableMedia.SiteKey
            Get
                Return PluginKey
            End Get
        End Property

        Public Property AccountName As String Implements IDownloadableMedia.AccountName
        Public Property Title As String Implements IDownloadableMedia.Title
        Public Property Size As Integer Implements IDownloadableMedia.Size
        Public Property Duration As TimeSpan Implements IDownloadableMedia.Duration
        Public Property Progress As Object Implements IDownloadableMedia.Progress
        Public Property Instance As IPluginContentProvider Implements IDownloadableMedia.Instance

        Public Property ThumbnailUrl As String Implements IDownloadableMedia.ThumbnailUrl
            Get
                Return _thumbnailUrl
            End Get
            Set(ByVal value As String)
                _thumbnailUrl = value
                RaiseEvent ThumbnailChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Public Property ThumbnailFile As String Implements IDownloadableMedia.ThumbnailFile
            Get
                Return _thumbnailFile
            End Get
            Set(ByVal value As String)
                _thumbnailFile = value
                RaiseEvent ThumbnailChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Public ReadOnly Property HasError As Boolean Implements IDownloadableMedia.HasError
            Get
                Return DownloadState = UserMediaStates.Missing
            End Get
        End Property

        Public ReadOnly Property Exists As Boolean Implements IDownloadableMedia.Exists
            Get
                Return Not String.IsNullOrWhiteSpace(File) AndAlso System.IO.File.Exists(File)
            End Get
        End Property

        Public Property Checked As Boolean Implements IDownloadableMedia.Checked
            Get
                Return _checked
            End Get
            Set(ByVal value As Boolean)
                _checked = value
                RaiseEvent CheckedChange(Me, EventArgs.Empty)
            End Set
        End Property

        Public Property ContentType As UserMediaTypes Implements IUserMedia.ContentType
        Public Property URL As String Implements IUserMedia.URL
        Public Property URL_BASE As String Implements IUserMedia.URL_BASE
        Public Property MD5 As String Implements IUserMedia.MD5
        Public Property File As String Implements IUserMedia.File

        Public Property DownloadState As UserMediaStates Implements IUserMedia.DownloadState
            Get
                Return _state
            End Get
            Set(ByVal value As UserMediaStates)
                _state = value
                RaiseEvent StateChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Public Property PostID As String Implements IUserMedia.PostID
        Public Property PostDate As Date? Implements IUserMedia.PostDate
        Public Property PostText As String Implements IUserMedia.PostText
        Public Property PostTextFile As String Implements IUserMedia.PostTextFile
        Public Property PostTextFileSpecialFolder As Boolean Implements IUserMedia.PostTextFileSpecialFolder
        Public Property SpecialFolder As String Implements IUserMedia.SpecialFolder
        Public Property Attempts As Integer Implements IUserMedia.Attempts
        Public Property [Object] As Object Implements IUserMedia.Object

        Public Sub Download(ByVal UseCookies As Boolean, ByVal Token As Threading.CancellationToken) Implements IDownloadableMedia.Download
            Token.ThrowIfCancellationRequested()
            If Not Instance Is Nothing Then Instance.DownloadSingleObject(Me, Token)
        End Sub

        Public Sub Delete(ByVal RemoveFiles As Boolean) Implements IDownloadableMedia.Delete
            If RemoveFiles AndAlso Exists Then
                Try
                    System.IO.File.Delete(File)
                Catch
                End Try
            End If
            DownloadState = UserMediaStates.Unknown
        End Sub

        Public Sub Load(ByVal filePath As String) Implements IDownloadableMedia.Load
            If String.IsNullOrWhiteSpace(filePath) Then Exit Sub
            File = filePath
            If String.IsNullOrWhiteSpace(Title) Then Title = Path.GetFileNameWithoutExtension(filePath)
        End Sub

        Public Sub Save() Implements IDownloadableMedia.Save
        End Sub

        Public Overrides Function ToString() As String Implements IDownloadableMedia.ToString
            Return $"{Title} [{Site}]"
        End Function

        Public Overloads Function ToString(ByVal ForMediaItem As Boolean) As String Implements IDownloadableMedia.ToString
            Return ToString()
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub
    End Class
End Namespace

