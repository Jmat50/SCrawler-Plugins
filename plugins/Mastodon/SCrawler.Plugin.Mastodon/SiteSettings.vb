Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports System.Text.RegularExpressions
Imports SCrawler.Plugin
Imports SCrawler.Plugin.Attributes

Namespace MastodonPlugin
    <Manifest(PluginKey), ReplaceInternalPluginAttribute(PluginKey)>
    Public Class SiteSettings
        Implements ISiteSettings

        Private Shared ReadOnly _icon As Icon = LoadEmbeddedIcon("site-favicon.ico")
        Private Shared ReadOnly _image As Image = _icon.ToBitmap()

        <PropertyOption(ControlText:="Default Domain", ControlToolTip:="Used when you enter only @user or user"), PXML>
        Public ReadOnly Property Domain As PropertyValue

        <PropertyOption(ControlText:="Listing URL Pattern", ControlToolTip:="Use {0} for full URL or domain"), PXML>
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
        Public Property Logger As ILogProvider Implements ISiteSettings.Logger
        Public Property AvailableText As String Implements ISiteSettings.AvailableText

        Public ReadOnly Property SubscriptionsAllowed As Boolean Implements ISiteSettings.SubscriptionsAllowed
            Get
                Return False
            End Get
        End Property

        Public Sub EnvironmentProgramsUpdated() Implements ISiteSettings.EnvironmentProgramsUpdated
        End Sub

        Public Function GetUserUrl(ByVal User As IPluginContentProvider) As String Implements ISiteSettings.GetUserUrl
            Dim candidates As String() = {User.Options, User.NameTrue, User.Name, User.ID}
            For Each candidate As String In candidates
                Dim resolved As String = NormalizeToAbsoluteUrl(candidate)
                If Not String.IsNullOrWhiteSpace(resolved) Then Return resolved
            Next
            Return String.Empty
        End Function

        Public Function IsMyUser(ByVal UserURL As String) As ExchangeOptions Implements ISiteSettings.IsMyUser
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(UserURL, UriKind.Absolute, uri) Then Return Nothing

            If IsStatusPath(uri.AbsolutePath) Then Return Nothing
            If Not IsProfilePath(uri.AbsolutePath) Then Return Nothing

            Dim userKey As String = ExtractUserKey(uri.AbsolutePath)
            If String.IsNullOrWhiteSpace(userKey) Then userKey = "mastodon"

            Return New ExchangeOptions With {
                .SiteName = Site,
                .HostKey = PluginKey,
                .UserName = userKey,
                .Options = uri.AbsoluteUri,
                .Exists = True
            }
        End Function

        Public Function IsMyImageVideo(ByVal URL As String) As ExchangeOptions Implements ISiteSettings.IsMyImageVideo
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(URL, UriKind.Absolute, uri) Then Return Nothing

            Dim isStatus As Boolean = IsStatusPath(uri.AbsolutePath)
            Dim isMedia As Boolean = IsDirectMediaPath(uri.AbsolutePath)
            If Not isStatus AndAlso Not isMedia Then Return Nothing

            Return New ExchangeOptions With {
                .SiteName = Site,
                .HostKey = PluginKey,
                .UserName = String.Empty,
                .Options = uri.AbsoluteUri,
                .Exists = True
            }
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
            If String.IsNullOrWhiteSpace(GetDomainOnly()) Then
                AvailableText = "Default domain is not configured."
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

        Private Function NormalizeToAbsoluteUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty
            Dim trimmed As String = value.Trim()

            Dim absolute As Uri = Nothing
            If Uri.TryCreate(trimmed, UriKind.Absolute, absolute) Then
                Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
            End If

            Dim m As Match = Regex.Match(trimmed, "^@?([A-Za-z0-9_\.]+)@([A-Za-z0-9\.\-]+)$")
            If m.Success Then
                Dim userName As String = m.Groups(1).Value
                Dim host As String = m.Groups(2).Value
                Return "https://" & host & "/@" & userName
            End If

            m = Regex.Match(trimmed, "^@?([A-Za-z0-9_\.]+)$")
            If m.Success Then
                Return "https://" & GetDomainOnly() & "/@" & m.Groups(1).Value
            End If

            If trimmed.StartsWith("/", StringComparison.Ordinal) Then
                Return "https://" & GetDomainOnly() & trimmed
            End If

            If trimmed.IndexOf("/", StringComparison.Ordinal) >= 0 Then
                Return "https://" & GetDomainOnly() & "/" & trimmed.TrimStart("/"c)
            End If

            Return String.Empty
        End Function

        Private Function GetDomainOnly() As String
            Dim d As String = CStr(Domain.Value)
            If String.IsNullOrWhiteSpace(d) Then d = DefaultDomain
            d = d.Trim()
            If d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then d = d.Substring(7)
            If d.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then d = d.Substring(8)
            Return d.Trim().Trim("/"c)
        End Function

        Private Shared Function IsProfilePath(ByVal path As String) As Boolean
            If String.IsNullOrWhiteSpace(path) Then Return False
            If Regex.IsMatch(path, "^/@[^/]+/?$", RegexOptions.IgnoreCase) Then Return True
            If Regex.IsMatch(path, "^/users/[^/]+/?$", RegexOptions.IgnoreCase) Then Return True
            Return False
        End Function

        Private Shared Function IsStatusPath(ByVal path As String) As Boolean
            If String.IsNullOrWhiteSpace(path) Then Return False
            If Regex.IsMatch(path, "^/@[^/]+/\d+/?$", RegexOptions.IgnoreCase) Then Return True
            If Regex.IsMatch(path, "^/web/statuses/\d+/?$", RegexOptions.IgnoreCase) Then Return True
            If Regex.IsMatch(path, "^/users/[^/]+/statuses/\d+/?$", RegexOptions.IgnoreCase) Then Return True
            Return False
        End Function

        Private Shared Function IsDirectMediaPath(ByVal path As String) As Boolean
            If String.IsNullOrWhiteSpace(path) Then Return False
            Return Regex.IsMatch(path, "\.(?:jpg|jpeg|png|gif|webp|mp4|webm|mov|m4v)$", RegexOptions.IgnoreCase)
        End Function

        Private Shared Function ExtractUserKey(ByVal path As String) As String
            Dim m As Match = Regex.Match(path, "^/@([^/]+)/?$", RegexOptions.IgnoreCase)
            If m.Success Then Return m.Groups(1).Value.TrimStart("@"c)

            m = Regex.Match(path, "^/users/([^/]+)/?$", RegexOptions.IgnoreCase)
            If m.Success Then Return m.Groups(1).Value

            Return String.Empty
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
