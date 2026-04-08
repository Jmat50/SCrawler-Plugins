Imports System.Drawing
Imports System.IO
Imports System.Reflection
Imports SCrawler.Plugin
Imports SCrawler.Plugin.Attributes

Namespace CoomerPlugin
    <Manifest(PluginKey)>
    Public Class SiteSettings
        Implements ISiteSettings

        Private Shared ReadOnly _icon As Icon = LoadEmbeddedIcon("site-favicon.ico")
        Private Shared ReadOnly _image As Image = _icon.ToBitmap()

        <PropertyOption(ControlText:="Domain", ControlToolTip:="Domain used to recognize profile URLs"), PXML>
        Public ReadOnly Property Domain As PropertyValue

        <PropertyOption(ControlText:="Profile URL Pattern", ControlToolTip:="Use string format pattern with {0}=creator and {1}=service"), PXML>
        Public ReadOnly Property UserUrlPattern As PropertyValue

        <PropertyOption(ControlText:="Default Service", ControlToolTip:="Service segment used in creator URLs (for example: onlyfans, fansly)"), PXML>
        Public ReadOnly Property Service As PropertyValue

        <PropertyOption(ControlText:="Authorization Header", ControlToolTip:="Optional auth token or header value"), PXML>
        Public ReadOnly Property AuthorizationHeader As PropertyValue

        Public Sub New()
            Me.New(String.Empty, False)
        End Sub

        Public Sub New(ByVal accountName As String, ByVal temporary As Boolean)
            Me.AccountName = accountName
            Me.Temporary = temporary
            Domain = New PropertyValue(DefaultDomain, GetType(String))
            UserUrlPattern = New PropertyValue(DefaultUserUrlPattern, GetType(String))
            Service = New PropertyValue(DefaultService, GetType(String))
            AuthorizationHeader = New PropertyValue(String.Empty, GetType(String))
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
            Dim pattern As String = CStr(UserUrlPattern.Value)
            If String.IsNullOrWhiteSpace(pattern) Then pattern = DefaultUserUrlPattern
            Dim namePart As String = User.NameTrue
            If String.IsNullOrWhiteSpace(namePart) Then namePart = User.Name
            If String.IsNullOrWhiteSpace(namePart) Then namePart = String.Empty

            Dim servicePart As String = CStr(Service.Value)
            If String.IsNullOrWhiteSpace(servicePart) Then servicePart = DefaultService

            If Not String.IsNullOrWhiteSpace(User.Options) Then
                servicePart = User.Options.Trim()
            End If

            Return String.Format(pattern, namePart, servicePart)
        End Function

        Public Function IsMyUser(ByVal UserURL As String) As ExchangeOptions Implements ISiteSettings.IsMyUser
            If String.IsNullOrWhiteSpace(UserURL) Then Return Nothing

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(UserURL, UriKind.Absolute, uri) Then Return Nothing

            Dim targetDomain As String = GetDomain()
            If targetDomain = String.Empty Then Return Nothing
            If Not uri.Host.EndsWith(targetDomain, StringComparison.OrdinalIgnoreCase) Then Return Nothing

            Dim service As String = Nothing
            Dim userName As String = Nothing
            If Not TryExtractServiceAndUser(uri, service, userName) Then Return Nothing

            Return New ExchangeOptions With {
                .SiteName = Site,
                .HostKey = PluginKey,
                .UserName = userName,
                .Options = service,
                .Exists = True
            }
        End Function

        Public Function IsMyImageVideo(ByVal URL As String) As ExchangeOptions Implements ISiteSettings.IsMyImageVideo
            If String.IsNullOrWhiteSpace(URL) Then Return Nothing

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(URL, UriKind.Absolute, uri) Then Return Nothing

            Dim targetDomain As String = GetDomain()
            If targetDomain = String.Empty Then Return Nothing

            If uri.Host.EndsWith(targetDomain, StringComparison.OrdinalIgnoreCase) Then
                Dim mediaPath As String = uri.AbsolutePath
                Return New ExchangeOptions With {
                    .SiteName = Site,
                    .HostKey = PluginKey,
                    .UserName = String.Empty,
                    .Options = mediaPath,
                    .Exists = mediaPath.StartsWith("/data/", StringComparison.OrdinalIgnoreCase) OrElse
                              mediaPath.StartsWith("/thumbnail/data/", StringComparison.OrdinalIgnoreCase)
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
            If GetDomain() = String.Empty Then
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
            c.UserUrlPattern.Value = UserUrlPattern.Value
            c.Service.Value = Service.Value
            c.AuthorizationHeader.Value = AuthorizationHeader.Value

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
            UserUrlPattern.Value = s.UserUrlPattern.Value
            Service.Value = s.Service.Value
            AuthorizationHeader.Value = s.AuthorizationHeader.Value
        End Sub

        Public Sub Reset() Implements ISiteSettings.Reset
            Domain.Value = DefaultDomain
            UserUrlPattern.Value = DefaultUserUrlPattern
            Service.Value = DefaultService
            AuthorizationHeader.Value = String.Empty
        End Sub

        Public Sub OpenSettingsForm() Implements ISiteSettings.OpenSettingsForm
        End Sub

        Public Sub UserOptions(ByRef Options As Object, ByVal OpenForm As Boolean) Implements ISiteSettings.UserOptions
            Options = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
        End Sub

        Private Function GetDomain() As String
            Dim value As String = CStr(Domain.Value)
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty
            Return value.Trim().TrimStart("."c)
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

        Private Shared Function TryExtractServiceAndUser(ByVal uri As Uri, ByRef service As String, ByRef userName As String) As Boolean
            service = Nothing
            userName = Nothing

            Dim parts() As String = uri.AbsolutePath.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If parts.Length < 3 Then Return False

            If Not parts(1).Equals("user", StringComparison.OrdinalIgnoreCase) Then Return False

            service = parts(0).Trim()
            userName = parts(2).Trim()

            Return service.Length > 0 AndAlso userName.Length > 0
        End Function
    End Class
End Namespace

