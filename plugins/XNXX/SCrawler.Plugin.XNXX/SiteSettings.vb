Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports SCrawler.Plugin
Imports SCrawler.Plugin.Attributes

Namespace XNXXPlugin
    <Manifest(PluginKey)>
    Public Class SiteSettings
        Implements ISiteSettings

        Private Shared ReadOnly _icon As Icon = LoadEmbeddedIcon("site-favicon.ico")
        Private Shared ReadOnly _image As Image = _icon.ToBitmap()

        <PropertyOption(ControlText:="Domain", ControlToolTip:="XNXX domain for URL checks"), PXML>
        Public ReadOnly Property Domain As PropertyValue

        <PropertyOption(ControlText:="Listing URL Pattern", ControlToolTip:="Use {0} for a path or search key"), PXML>
        Public ReadOnly Property ListingUrlPattern As PropertyValue

        Public Sub New()
            Me.New(String.Empty, False)
        End Sub

        Public Sub New(ByVal accountName As String, ByVal temporary As Boolean)
            Me.AccountName = accountName
            Me.Temporary = temporary
            Domain = New PropertyValue(DefaultDomain, GetType(String))
            ListingUrlPattern = New PropertyValue(DefaultListingUrlPattern, GetType(String))
        End Sub

        Public ReadOnly Property Icon As Icon Implements ISiteSettings.Icon
            Get
                Return _icon
            End Get
        End Property

        Public ReadOnly Property Image As Image Implements ISiteSettings.Image
            Get
                Return _image
            End Get
        End Property

        Public ReadOnly Property Site As String Implements ISiteSettings.Site
            Get
                Return SiteDisplayName
            End Get
        End Property

        Public Property CMDEncoding As String Implements ISiteSettings.CMDEncoding
        Public Property EnvironmentPrograms As IEnumerable(Of String) Implements ISiteSettings.EnvironmentPrograms
        Public Property UserAgentDefault As String Implements ISiteSettings.UserAgentDefault
        Public Property AccountName As String Implements ISiteSettings.AccountName
        Public Property Temporary As Boolean Implements ISiteSettings.Temporary
        Public Property DefaultInstance As ISiteSettings Implements ISiteSettings.DefaultInstance

        Public ReadOnly Property SubscriptionsAllowed As Boolean Implements ISiteSettings.SubscriptionsAllowed
            Get
                Return False
            End Get
        End Property

        Public Property Logger As ILogProvider Implements ISiteSettings.Logger
        Public Property AvailableText As String Implements ISiteSettings.AvailableText

        Public Sub EnvironmentProgramsUpdated() Implements ISiteSettings.EnvironmentProgramsUpdated
        End Sub

        Public Function GetUserUrl(ByVal User As IPluginContentProvider) As String Implements ISiteSettings.GetUserUrl
            Dim baseUrl As String = GetBaseUrl()
            If String.IsNullOrWhiteSpace(baseUrl) Then Return String.Empty

            If Not String.IsNullOrWhiteSpace(User.Options) Then
                Dim u As String = NormalizeToAbsoluteUrl(User.Options)
                If Not String.IsNullOrWhiteSpace(u) Then Return u
            End If

            Dim key As String = User.NameTrue
            If String.IsNullOrWhiteSpace(key) Then key = User.Name
            If String.IsNullOrWhiteSpace(key) Then key = User.ID
            If String.IsNullOrWhiteSpace(key) Then Return String.Empty

            Dim pattern As String = CStr(ListingUrlPattern.Value)
            If String.IsNullOrWhiteSpace(pattern) Then pattern = DefaultListingUrlPattern

            Return String.Format(pattern, key.Trim())
        End Function

        Public Function IsMyUser(ByVal UserURL As String) As ExchangeOptions Implements ISiteSettings.IsMyUser
            If String.IsNullOrWhiteSpace(UserURL) Then Return Nothing

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(UserURL, UriKind.Absolute, uri) Then Return Nothing

            Dim targetDomain As String = GetDomain()
            If targetDomain = String.Empty Then Return Nothing
            If Not HostMatchesDomain(uri.Host, targetDomain) Then Return Nothing

            Dim relative As String = BuildRelativeWithQuery(uri)
            If String.IsNullOrWhiteSpace(relative) OrElse relative = "/" Then Return Nothing
            If IsVideoLikePath(uri.AbsolutePath) Then Return Nothing

            Dim key As String = ExtractUserKey(uri.AbsolutePath)
            If String.IsNullOrWhiteSpace(key) Then key = "xnxx"

            Return New ExchangeOptions With {
                .SiteName = Site,
                .HostKey = PluginKey,
                .UserName = key,
                .Options = relative,
                .Exists = True
            }
        End Function

        Public Function IsMyImageVideo(ByVal URL As String) As ExchangeOptions Implements ISiteSettings.IsMyImageVideo
            If String.IsNullOrWhiteSpace(URL) Then Return Nothing

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(URL, UriKind.Absolute, uri) Then Return Nothing

            Dim host As String = uri.Host
            Dim path As String = uri.AbsolutePath

            Dim targetDomain As String = GetDomain()
            If targetDomain <> String.Empty AndAlso HostMatchesDomain(host, targetDomain) Then
                If IsVideoLikePath(path) Then
                    Return New ExchangeOptions With {
                        .SiteName = Site,
                        .HostKey = PluginKey,
                        .UserName = String.Empty,
                        .Options = BuildRelativeWithQuery(uri),
                        .Exists = True
                    }
                End If
            End If

            Dim isMediaHost As Boolean = host.IndexOf("xnxx-cdn.com", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                         host.IndexOf("xnxx.com", StringComparison.OrdinalIgnoreCase) >= 0

            If isMediaHost AndAlso (path.IndexOf(".mp4", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                    path.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0) Then
                Return New ExchangeOptions With {
                    .SiteName = Site,
                    .HostKey = PluginKey,
                    .UserName = String.Empty,
                    .Options = URL,
                    .Exists = True
                }
            End If

            Return Nothing
        End Function

        Public Function GetInstance(ByVal What As ISiteSettings.Download) As IPluginContentProvider Implements ISiteSettings.GetInstance
            Return New UserData
        End Function

        Public Function GetSingleMediaInstance(ByVal URL As String, ByVal OutputFile As String) As IDownloadableMedia Implements ISiteSettings.GetSingleMediaInstance
            Return New CustomDownloadableMedia(URL, OutputFile)
        End Function

        Public Function GetUserPostUrl(ByVal User As IPluginContentProvider, ByVal Media As IUserMedia) As String Implements ISiteSettings.GetUserPostUrl
            If Not String.IsNullOrWhiteSpace(Media.URL_BASE) Then Return Media.URL_BASE
            Return Media.URL
        End Function

        Public Sub BeginInit() Implements ISiteSettings.BeginInit
        End Sub

        Public Sub EndInit() Implements ISiteSettings.EndInit
        End Sub

        Public Sub BeginUpdate() Implements ISiteSettings.BeginUpdate
        End Sub

        Public Sub EndUpdate() Implements ISiteSettings.EndUpdate
        End Sub

        Public Sub BeginEdit() Implements ISiteSettings.BeginEdit
        End Sub

        Public Sub EndEdit() Implements ISiteSettings.EndEdit
        End Sub

        Public Function Available(ByVal What As ISiteSettings.Download, ByVal Silent As Boolean) As Boolean Implements ISiteSettings.Available
            If String.IsNullOrWhiteSpace(GetDomain()) Then
                AvailableText = "Domain is not configured."
                Return False
            End If

            AvailableText = String.Empty
            Return True
        End Function

        Public Function ReadyToDownload(ByVal What As ISiteSettings.Download) As Boolean Implements ISiteSettings.ReadyToDownload
            Return Available(What, True)
        End Function

        Public Sub DownloadStarted(ByVal What As ISiteSettings.Download) Implements ISiteSettings.DownloadStarted
        End Sub

        Public Sub BeforeStartDownload(ByVal User As Object, ByVal What As ISiteSettings.Download) Implements ISiteSettings.BeforeStartDownload
        End Sub

        Public Sub AfterDownload(ByVal User As Object, ByVal What As ISiteSettings.Download) Implements ISiteSettings.AfterDownload
        End Sub

        Public Sub DownloadDone(ByVal What As ISiteSettings.Download) Implements ISiteSettings.DownloadDone
            AvailableText = String.Empty
        End Sub

        Public Function Clone(ByVal Full As Boolean) As ISiteSettings Implements ISiteSettings.Clone
            Dim c As New SiteSettings(AccountName, Temporary) With {
                .CMDEncoding = CMDEncoding,
                .UserAgentDefault = UserAgentDefault
            }

            c.Domain.Value = Domain.Value
            c.ListingUrlPattern.Value = ListingUrlPattern.Value
            Return c
        End Function

        Public Sub Delete() Implements ISiteSettings.Delete
        End Sub

        Public Overloads Sub Update() Implements ISiteSettings.Update
        End Sub

        Public Overloads Sub Update(ByVal Source As ISiteSettings) Implements ISiteSettings.Update
            Dim s As SiteSettings = TryCast(Source, SiteSettings)
            If s Is Nothing Then Exit Sub

            CMDEncoding = s.CMDEncoding
            UserAgentDefault = s.UserAgentDefault
            AccountName = s.AccountName
            Temporary = s.Temporary
            Domain.Value = s.Domain.Value
            ListingUrlPattern.Value = s.ListingUrlPattern.Value
        End Sub

        Public Sub Reset() Implements ISiteSettings.Reset
            Domain.Value = DefaultDomain
            ListingUrlPattern.Value = DefaultListingUrlPattern
        End Sub

        Public Sub OpenSettingsForm() Implements ISiteSettings.OpenSettingsForm
        End Sub

        Public Sub UserOptions(ByRef Options As Object, ByVal OpenForm As Boolean) Implements ISiteSettings.UserOptions
            Options = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub

        Private Function GetBaseUrl() As String
            Dim d As String = GetDomain()
            If d = String.Empty Then Return String.Empty
            If d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse d.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                Return d.TrimEnd("/"c)
            End If
            Return "https://" & d.TrimEnd("/"c)
        End Function

        Private Function GetDomain() As String
            Dim value As String = CStr(Domain.Value)
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            value = value.Trim()
            If value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then value = value.Substring(7)
            If value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then value = value.Substring(8)
            Return value.Trim().Trim("/"c)
        End Function

        Private Shared Function BuildRelativeWithQuery(ByVal uri As Uri) As String
            Dim relative As String = uri.AbsolutePath
            If String.IsNullOrWhiteSpace(relative) Then relative = "/"
            If Not String.IsNullOrWhiteSpace(uri.Query) Then relative &= uri.Query
            Return relative
        End Function

        Private Shared Function IsVideoLikePath(ByVal absolutePath As String) As Boolean
            If String.IsNullOrWhiteSpace(absolutePath) Then Return False

            Return absolutePath.StartsWith(VideoPathPrefix, StringComparison.OrdinalIgnoreCase) OrElse
                   absolutePath.StartsWith("/video/", StringComparison.OrdinalIgnoreCase) OrElse
                   absolutePath.StartsWith("/embedframe/", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Function NormalizeToAbsoluteUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim trimmed As String = value.Trim()
            Dim absolute As Uri = Nothing
            If Uri.TryCreate(trimmed, UriKind.Absolute, absolute) Then
                Dim targetDomain As String = GetDomain()
                If targetDomain <> String.Empty AndAlso HostMatchesDomain(absolute.Host, targetDomain) Then
                    Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
                End If
            End If

            If trimmed.StartsWith("/", StringComparison.Ordinal) Then
                Return GetBaseUrl() & trimmed
            End If

            If trimmed.IndexOf("/", StringComparison.Ordinal) >= 0 Then
                Return GetBaseUrl() & "/" & trimmed.TrimStart("/"c)
            End If

            Return String.Format(CStr(ListingUrlPattern.Value), trimmed)
        End Function

        Private Shared Function ExtractUserKey(ByVal absolutePath As String) As String
            If String.IsNullOrWhiteSpace(absolutePath) Then Return String.Empty

            Dim parts() As String = absolutePath.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length = 0 Then Return String.Empty

            Dim key As String = parts(parts.Length - 1)
            If String.IsNullOrWhiteSpace(key) AndAlso parts.Length > 1 Then key = parts(parts.Length - 2)
            If String.IsNullOrWhiteSpace(key) Then key = parts(0)

            Return key.Trim()
        End Function

        Private Shared Function HostMatchesDomain(ByVal host As String, ByVal configuredDomain As String) As Boolean
            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(configuredDomain) Then Return False

            If host.Equals(configuredDomain, StringComparison.OrdinalIgnoreCase) Then Return True
            If host.EndsWith("." & configuredDomain, StringComparison.OrdinalIgnoreCase) Then Return True

            If configuredDomain.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then
                Dim noWww As String = configuredDomain.Substring(4)
                If host.Equals(noWww, StringComparison.OrdinalIgnoreCase) Then Return True
                If host.EndsWith("." & noWww, StringComparison.OrdinalIgnoreCase) Then Return True
            End If

            Return False
        End Function

        Private Shared Function LoadEmbeddedIcon(ByVal fileName As String) As Icon
            Try
                Dim assemblyRef As Assembly = GetType(SiteSettings).Assembly
                For Each resourceName As String In assemblyRef.GetManifestResourceNames()
                    If Not resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) Then Continue For

                    Using stream As Stream = assemblyRef.GetManifestResourceStream(resourceName)
                        If stream Is Nothing Then Continue For

                        Using loaded As New Icon(stream)
                            Return DirectCast(loaded.Clone(), Icon)
                        End Using
                    End Using
                Next
            Catch
            End Try

            Return DirectCast(SystemIcons.Application.Clone(), Icon)
        End Function
    End Class
End Namespace
