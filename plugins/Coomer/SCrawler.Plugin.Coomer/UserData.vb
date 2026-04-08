Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Threading
Imports System.Web.Script.Serialization
Imports SCrawler.Plugin

Namespace CoomerPlugin
    Public Class UserData
        Implements IPluginContentProvider

        Public Event ProgressChanged(ByVal Value As Integer) Implements IPluginContentProvider.ProgressChanged
        Public Event ProgressMaximumChanged(ByVal Value As Integer, ByVal Add As Boolean) Implements IPluginContentProvider.ProgressMaximumChanged
        Public Event ProgressPreChanged As IPluginContentProvider.ProgressChangedEventHandler Implements IPluginContentProvider.ProgressPreChanged
        Public Event ProgressPreMaximumChanged As IPluginContentProvider.ProgressMaximumChangedEventHandler Implements IPluginContentProvider.ProgressPreMaximumChanged

        Public Property Thrower As IThrower Implements IPluginContentProvider.Thrower
        Public Property LogProvider As ILogProvider Implements IPluginContentProvider.LogProvider
        Public Property Settings As ISiteSettings Implements IPluginContentProvider.Settings
        Public Property AccountName As String Implements IPluginContentProvider.AccountName
        Public Property Name As String Implements IPluginContentProvider.Name
        Public Property NameTrue As String Implements IPluginContentProvider.NameTrue
        Public Property ID As String Implements IPluginContentProvider.ID
        Public Property Options As String Implements IPluginContentProvider.Options
        Public Property ParseUserMediaOnly As Boolean Implements IPluginContentProvider.ParseUserMediaOnly
        Public Property UserDescription As String Implements IPluginContentProvider.UserDescription
        Public Property ExistingContentList As List(Of IUserMedia) Implements IPluginContentProvider.ExistingContentList
        Public Property TempPostsList As List(Of String) Implements IPluginContentProvider.TempPostsList
        Public Property TempMediaList As List(Of IUserMedia) Implements IPluginContentProvider.TempMediaList
        Public Property UserExists As Boolean Implements IPluginContentProvider.UserExists
        Public Property UserSuspended As Boolean Implements IPluginContentProvider.UserSuspended
        Public Property IsSavedPosts As Boolean Implements IPluginContentProvider.IsSavedPosts
        Public Property IsSubscription As Boolean Implements IPluginContentProvider.IsSubscription
        Public Property SeparateVideoFolder As Boolean Implements IPluginContentProvider.SeparateVideoFolder
        Public Property DataPath As String Implements IPluginContentProvider.DataPath
        Public Property PostsNumberLimit As Integer? Implements IPluginContentProvider.PostsNumberLimit
        Public Property DownloadDateFrom As Date? Implements IPluginContentProvider.DownloadDateFrom
        Public Property DownloadDateTo As Date? Implements IPluginContentProvider.DownloadDateTo

        Private _serviceFromExchange As String
        Private _userFromExchange As String

        Public Sub New()
            ExistingContentList = New List(Of IUserMedia)
            TempPostsList = New List(Of String)
            TempMediaList = New List(Of IUserMedia)
            UserExists = True
            UserSuspended = False
        End Sub

        Public Function ExchangeOptionsGet() As Object Implements IPluginContentProvider.ExchangeOptionsGet
            Return New ExchangeOptions With {
                .SiteName = SiteDisplayName,
                .HostKey = PluginKey,
                .UserName = Name,
                .Options = Options,
                .Exists = Not String.IsNullOrWhiteSpace(Name)
            }
        End Function

        Public Sub ExchangeOptionsSet(ByVal Obj As Object) Implements IPluginContentProvider.ExchangeOptionsSet
            If Obj Is Nothing Then Exit Sub

            If TypeOf Obj Is ExchangeOptions Then
                Dim ex As ExchangeOptions = DirectCast(Obj, ExchangeOptions)
                If Not String.IsNullOrWhiteSpace(ex.Options) Then _serviceFromExchange = ex.Options
                If Not String.IsNullOrWhiteSpace(ex.UserName) Then _userFromExchange = ex.UserName
            ElseIf TypeOf Obj Is String Then
                _serviceFromExchange = CStr(Obj)
            End If
        End Sub

        Public Sub XmlFieldsSet(ByVal Fields As List(Of KeyValuePair(Of String, String))) Implements IPluginContentProvider.XmlFieldsSet
        End Sub

        Public Function XmlFieldsGet() As List(Of KeyValuePair(Of String, String)) Implements IPluginContentProvider.XmlFieldsGet
            Return Nothing
        End Function

        Public Sub GetMedia(ByVal Token As CancellationToken) Implements IPluginContentProvider.GetMedia
            TempPostsList.Clear()
            TempMediaList.Clear()
            UserExists = True
            UserSuspended = False

            Dim context As CreatorContext = ResolveCreatorContext()
            If String.IsNullOrWhiteSpace(context.UserName) Then
                UserExists = False
                LogProvider?.Add("Coomer: could not resolve creator id/name from user data.")
                Exit Sub
            End If

            If String.IsNullOrWhiteSpace(context.Service) Then context.Service = DefaultService

            Try
                EnsureProfileExists(context, Token)
            Catch ex As WebException
                Dim status As HttpStatusCode? = GetStatusCode(ex)
                If status.HasValue AndAlso status.Value = HttpStatusCode.NotFound Then
                    UserExists = False
                    LogProvider?.Add("Coomer: profile not found.")
                Else
                    LogProvider?.Add(ex, "[CoomerPlugin.GetMedia] profile request failed")
                End If
                Exit Sub
            Catch ex As Exception
                LogProvider?.Add(ex, "[CoomerPlugin.GetMedia] profile parse failed")
                Exit Sub
            End Try

            Dim seenPostIds As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim stopRequested As Boolean = False
            Dim offset As Integer = 0

            Do While Not stopRequested
                Token.ThrowIfCancellationRequested()
                Thrower?.ThrowAny()

                Dim posts As List(Of CoomerPost) = Nothing
                Try
                    posts = GetPostsPage(context, offset)
                Catch ex As WebException
                    Dim status As HttpStatusCode? = GetStatusCode(ex)
                    If status.HasValue AndAlso status.Value = HttpStatusCode.NotFound Then
                        UserExists = False
                        LogProvider?.Add("Coomer: creator posts endpoint was not found.")
                    ElseIf status.HasValue AndAlso CInt(status.Value) = 429 Then
                        LogProvider?.Add("Coomer: API rate limited this request. Retry in a minute.")
                    Else
                        LogProvider?.Add(ex, String.Format("[CoomerPlugin.GetMedia] posts request failed at offset {0}", offset))
                    End If
                    Exit Do
                Catch ex As Exception
                    LogProvider?.Add(ex, String.Format("[CoomerPlugin.GetMedia] posts parse failed at offset {0}", offset))
                    Exit Do
                End Try

                If posts Is Nothing OrElse posts.Count = 0 Then Exit Do

                For Each post As CoomerPost In posts
                    Token.ThrowIfCancellationRequested()
                    Thrower?.ThrowAny()

                    If post Is Nothing Then Continue For
                    If String.IsNullOrWhiteSpace(post.id) Then Continue For
                    If Not seenPostIds.Add(post.id) Then Continue For

                    Dim postDate As Date? = ParsePostDate(post.published)
                    If Not IsPostDateAllowed(postDate) Then Continue For

                    TempPostsList.Add(post.id)
                    AddPostMedia(context, post, postDate)

                    If PostsNumberLimit.HasValue AndAlso TempPostsList.Count >= PostsNumberLimit.Value Then
                        stopRequested = True
                        Exit For
                    End If
                Next

                If stopRequested Then Exit Do
                If posts.Count < ApiPageSize Then Exit Do

                offset += ApiPageSize
            Loop

            LogProvider?.Add(String.Format("Coomer: parsed {0} post(s), collected {1} media item(s).", TempPostsList.Count, TempMediaList.Count))
        End Sub

        Public Sub Download(ByVal Token As CancellationToken) Implements IPluginContentProvider.Download
            If TempMediaList Is Nothing OrElse TempMediaList.Count = 0 Then Exit Sub

            Dim outputDir As String = ResolveOutputPath()
            Directory.CreateDirectory(outputDir)

            RaiseEvent ProgressMaximumChanged(TempMediaList.Count, False)

            For i As Integer = 0 To TempMediaList.Count - 1
                Token.ThrowIfCancellationRequested()
                Thrower?.ThrowAny()

                Dim media As IUserMedia = TempMediaList(i)

                Try
                    If String.IsNullOrWhiteSpace(media.URL) Then
                        media.DownloadState = UserMediaStates.Missing
                    Else
                        Dim targetFile As String = BuildTargetFilePath(media, outputDir, i + 1)

                        Using wc As New WebClient
                            ApplyDownloadHeaders(wc)
                            wc.DownloadFile(media.URL, targetFile)
                        End Using

                        media.File = targetFile
                        media.DownloadState = UserMediaStates.Downloaded
                    End If
                Catch ex As Exception
                    media.DownloadState = UserMediaStates.Missing
                    media.Attempts += 1
                    LogProvider?.Add(ex, String.Format("[CoomerPlugin.Download] {0}", media.URL))
                End Try

                TempMediaList(i) = media
                RaiseEvent ProgressChanged(i + 1)
            Next
        End Sub

        Public Sub DownloadSingleObject(ByVal Data As IDownloadableMedia, ByVal Token As CancellationToken) Implements IPluginContentProvider.DownloadSingleObject
            If Data Is Nothing Then Exit Sub

            TempMediaList.Clear()
            TempMediaList.Add(New PluginUserMedia With {
                              .ContentType = Data.ContentType,
                              .URL = Data.URL,
                              .URL_BASE = Data.URL_BASE,
                              .File = Data.File,
                              .PostID = Data.PostID,
                              .PostDate = Data.PostDate,
                              .DownloadState = Data.DownloadState})

            Download(Token)

            If TempMediaList.Count > 0 Then
                Dim result As IUserMedia = TempMediaList(0)
                Data.File = result.File
                Data.DownloadState = result.DownloadState
            End If
        End Sub

        Public Sub ResetHistoryData() Implements IPluginContentProvider.ResetHistoryData
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ExistingContentList?.Clear()
            TempPostsList?.Clear()
            TempMediaList?.Clear()
        End Sub

        Private Function ResolveOutputPath() As String
            If Not String.IsNullOrWhiteSpace(DataPath) Then Return DataPath
            Return Path.Combine(Environment.CurrentDirectory, "CoomerDownloads")
        End Function

        Private Function ResolveCreatorContext() As CreatorContext
            Dim context As New CreatorContext With {
                .Service = CleanSegment(Options),
                .UserName = CleanSegment(NameTrue)
            }

            If String.IsNullOrWhiteSpace(context.UserName) Then context.UserName = CleanSegment(Name)
            If String.IsNullOrWhiteSpace(context.UserName) Then context.UserName = CleanSegment(ID)
            If String.IsNullOrWhiteSpace(context.UserName) Then context.UserName = CleanSegment(_userFromExchange)

            If String.IsNullOrWhiteSpace(context.Service) Then context.Service = CleanSegment(_serviceFromExchange)

            Dim candidates As String() = {NameTrue, Name, ID, _userFromExchange}
            For Each candidate As String In candidates
                Dim parsedService As String = Nothing
                Dim parsedUser As String = Nothing
                If TryParseCreatorReference(candidate, parsedService, parsedUser) Then
                    If String.IsNullOrWhiteSpace(context.Service) Then context.Service = parsedService
                    If String.IsNullOrWhiteSpace(context.UserName) Then context.UserName = parsedUser
                    Exit For
                End If
            Next

            If String.IsNullOrWhiteSpace(context.Service) Then
                Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
                If settingsData IsNot Nothing Then
                    context.Service = CleanSegment(CStr(settingsData.Service.Value))
                End If
            End If

            If String.IsNullOrWhiteSpace(context.Service) Then context.Service = DefaultService
            Return context
        End Function

        Private Sub EnsureProfileExists(ByRef context As CreatorContext, ByVal token As CancellationToken)
            token.ThrowIfCancellationRequested()
            Thrower?.ThrowAny()

            Dim profilePath As String = String.Format("{0}/user/{1}/profile", context.Service, context.UserName)
            Dim json As String = DownloadApiString(profilePath)
            If String.IsNullOrWhiteSpace(json) Then Exit Sub

            Dim serializer As JavaScriptSerializer = CreateSerializer()
            Dim profile As CoomerProfile = serializer.Deserialize(Of CoomerProfile)(json)
            If profile Is Nothing Then Exit Sub

            If Not String.IsNullOrWhiteSpace(profile.id) Then
                context.UserName = profile.id
                If String.IsNullOrWhiteSpace(NameTrue) Then NameTrue = profile.id
            End If

            If Not String.IsNullOrWhiteSpace(profile.service) Then
                context.Service = profile.service
            End If

            If String.IsNullOrWhiteSpace(Name) AndAlso Not String.IsNullOrWhiteSpace(profile.name) Then
                Name = profile.name
            End If
        End Sub

        Private Function GetPostsPage(ByVal context As CreatorContext, ByVal offset As Integer) As List(Of CoomerPost)
            Dim relative As String = String.Format("{0}/user/{1}/posts", context.Service, context.UserName)
            If offset > 0 Then
                relative &= "?o=" & offset.ToString()
            End If

            Dim json As String = DownloadApiString(relative)
            If String.IsNullOrWhiteSpace(json) Then Return New List(Of CoomerPost)

            Dim serializer As JavaScriptSerializer = CreateSerializer()

            Try
                Dim directPosts As List(Of CoomerPost) = serializer.Deserialize(Of List(Of CoomerPost))(json)
                If directPosts IsNot Nothing Then Return directPosts
            Catch
            End Try

            Dim envelope As CoomerPostsEnvelope = serializer.Deserialize(Of CoomerPostsEnvelope)(json)
            If envelope IsNot Nothing AndAlso envelope.results IsNot Nothing Then
                Return envelope.results
            End If

            Return New List(Of CoomerPost)
        End Function

        Private Sub AddPostMedia(ByVal context As CreatorContext, ByVal post As CoomerPost, ByVal postDate As Date?)
            Dim seenUrls As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            AddSingleMedia(context, post, post.file, seenUrls, postDate)

            If post.attachments Is Nothing Then Exit Sub
            For Each attachment As CoomerFile In post.attachments
                AddSingleMedia(context, post, attachment, seenUrls, postDate)
            Next
        End Sub

        Private Sub AddSingleMedia(ByVal context As CreatorContext,
                                   ByVal post As CoomerPost,
                                   ByVal source As CoomerFile,
                                   ByVal seenUrls As HashSet(Of String),
                                   ByVal postDate As Date?)
            If source Is Nothing Then Exit Sub
            If String.IsNullOrWhiteSpace(source.path) Then Exit Sub

            Dim mediaUrl As String = BuildMediaUrl(source.path)
            If String.IsNullOrWhiteSpace(mediaUrl) Then Exit Sub
            If Not seenUrls.Add(mediaUrl) Then Exit Sub

            Dim fileName As String = source.name
            If String.IsNullOrWhiteSpace(fileName) Then fileName = Path.GetFileName(source.path)
            If String.IsNullOrWhiteSpace(fileName) Then
                Dim mediaUri As Uri = Nothing
                If Uri.TryCreate(mediaUrl, UriKind.Absolute, mediaUri) Then
                    fileName = Path.GetFileName(mediaUri.LocalPath)
                End If
            End If

            Dim mediaType As UserMediaTypes = InferMediaType(source.name, source.path)
            Dim postText As String = post.substring
            If String.IsNullOrWhiteSpace(postText) Then postText = post.title

            Dim media As New PluginUserMedia With {
                .ContentType = mediaType,
                .URL = mediaUrl,
                .URL_BASE = BuildPostUrl(context, post.id),
                .File = fileName,
                .PostID = post.id,
                .PostDate = postDate,
                .PostText = postText,
                .DownloadState = UserMediaStates.Unknown
            }

            TempMediaList.Add(media)
        End Sub

        Private Function BuildPostUrl(ByVal context As CreatorContext, ByVal postId As String) As String
            If String.IsNullOrWhiteSpace(context.Service) Then Return String.Empty
            If String.IsNullOrWhiteSpace(context.UserName) Then Return String.Empty
            If String.IsNullOrWhiteSpace(postId) Then Return String.Empty

            Return String.Format("{0}/{1}/user/{2}/post/{3}",
                                 GetSiteBaseUrl(),
                                 context.Service,
                                 context.UserName,
                                 postId)
        End Function

        Private Function BuildMediaUrl(ByVal mediaPath As String) As String
            If String.IsNullOrWhiteSpace(mediaPath) Then Return String.Empty

            If Uri.IsWellFormedUriString(mediaPath, UriKind.Absolute) Then
                Return mediaPath
            End If

            Dim normalized As String = mediaPath.Trim()
            If normalized.StartsWith("/data/", StringComparison.OrdinalIgnoreCase) Then
                Return GetSiteBaseUrl() & normalized
            End If

            If normalized.StartsWith("/", StringComparison.Ordinal) Then
                Return GetSiteBaseUrl() & "/data" & normalized
            End If

            Return GetSiteBaseUrl() & "/data/" & normalized
        End Function

        Private Function DownloadApiString(ByVal relativePath As String) As String
            Dim absoluteUrl As String = String.Format("{0}{1}/{2}",
                                                      GetSiteBaseUrl(),
                                                      ApiBasePath,
                                                      relativePath.TrimStart("/"c))

            Using wc As WebClient = CreateApiWebClient()
                Return wc.DownloadString(absoluteUrl)
            End Using
        End Function

        Private Function CreateApiWebClient() As WebClient
            Dim wc As New PluginWebClient With {
                .Encoding = Encoding.UTF8
            }

            wc.Headers(HttpRequestHeader.Accept) = ApiScraperAccept
            wc.Headers(HttpRequestHeader.UserAgent) = ResolveUserAgent()
            ApplyOptionalAuthorizationHeader(wc)

            Return wc
        End Function

        Private Sub ApplyDownloadHeaders(ByVal wc As WebClient)
            wc.Headers(HttpRequestHeader.UserAgent) = ResolveUserAgent()
        End Sub

        Private Function ResolveUserAgent() As String
            If Settings IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Settings.UserAgentDefault) Then
                Return Settings.UserAgentDefault
            End If

            Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
        End Function

        Private Sub ApplyOptionalAuthorizationHeader(ByVal wc As WebClient)
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData Is Nothing Then Exit Sub

            Dim rawHeader As String = CStr(settingsData.AuthorizationHeader.Value)
            If String.IsNullOrWhiteSpace(rawHeader) Then Exit Sub

            Dim separator As Integer = rawHeader.IndexOf(":"c)
            If separator > 0 Then
                Dim headerName As String = rawHeader.Substring(0, separator).Trim()
                Dim headerValue As String = rawHeader.Substring(separator + 1).Trim()
                If headerName.Length > 0 AndAlso headerValue.Length > 0 Then
                    wc.Headers(headerName) = headerValue
                End If
            Else
                wc.Headers(HttpRequestHeader.Authorization) = rawHeader.Trim()
            End If
        End Sub

        Private Function GetSiteBaseUrl() As String
            Dim domainValue As String = DefaultDomain
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData IsNot Nothing Then
                Dim configured As String = CStr(settingsData.Domain.Value)
                If Not String.IsNullOrWhiteSpace(configured) Then
                    domainValue = configured.Trim()
                End If
            End If

            If Not domainValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso
               Not domainValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                domainValue = "https://" & domainValue.TrimStart("/"c)
            End If

            Return domainValue.TrimEnd("/"c)
        End Function

        Private Shared Function CreateSerializer() As JavaScriptSerializer
            Return New JavaScriptSerializer With {
                .MaxJsonLength = Integer.MaxValue
            }
        End Function

        Private Shared Function ParsePostDate(ByVal value As String) As Date?
            If String.IsNullOrWhiteSpace(value) Then Return Nothing

            Dim parsed As Date
            If Date.TryParse(value, parsed) Then Return parsed
            Return Nothing
        End Function

        Private Function IsPostDateAllowed(ByVal postDate As Date?) As Boolean
            If Not postDate.HasValue Then Return True

            If DownloadDateFrom.HasValue AndAlso postDate.Value.Date < DownloadDateFrom.Value.Date Then Return False
            If DownloadDateTo.HasValue AndAlso postDate.Value.Date > DownloadDateTo.Value.Date Then Return False

            Return True
        End Function

        Private Shared Function InferMediaType(ByVal fileName As String, ByVal mediaPath As String) As UserMediaTypes
            Dim ext As String = Path.GetExtension(fileName)
            If String.IsNullOrWhiteSpace(ext) Then ext = Path.GetExtension(mediaPath)
            ext = If(ext, String.Empty).ToLowerInvariant()

            Select Case ext
                Case ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".heic", ".heif"
                    Return UserMediaTypes.Picture
                Case ".gif"
                    Return UserMediaTypes.GIF
                Case ".mp4", ".webm", ".mov", ".m4v", ".mkv", ".avi"
                    Return UserMediaTypes.Video
                Case ".mp3", ".ogg", ".wav", ".m4a", ".flac", ".aac"
                    Return UserMediaTypes.Audio
                Case Else
                    Return UserMediaTypes.Undefined
            End Select
        End Function

        Private Shared Function CleanSegment(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim clean As String = value.Trim()
            Dim qIndex As Integer = clean.IndexOf("?"c)
            If qIndex >= 0 Then clean = clean.Substring(0, qIndex)
            clean = clean.Trim().Trim("/"c)
            Return clean
        End Function

        Private Shared Function TryParseCreatorReference(ByVal value As String, ByRef service As String, ByRef userName As String) As Boolean
            service = Nothing
            userName = Nothing

            If String.IsNullOrWhiteSpace(value) Then Return False

            Dim parsedUri As Uri = Nothing
            If Uri.TryCreate(value, UriKind.Absolute, parsedUri) Then
                Dim parts() As String = parsedUri.AbsolutePath.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
                If parts.Length >= 3 AndAlso parts(1).Equals("user", StringComparison.OrdinalIgnoreCase) Then
                    service = CleanSegment(parts(0))
                    userName = CleanSegment(parts(2))
                    Return service.Length > 0 AndAlso userName.Length > 0
                End If
            End If

            Dim raw As String = value.Trim()
            Dim slashParts() As String = raw.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If slashParts.Length >= 3 AndAlso slashParts(1).Equals("user", StringComparison.OrdinalIgnoreCase) Then
                service = CleanSegment(slashParts(0))
                userName = CleanSegment(slashParts(2))
                Return service.Length > 0 AndAlso userName.Length > 0
            End If

            If slashParts.Length = 2 Then
                service = CleanSegment(slashParts(0))
                userName = CleanSegment(slashParts(1))
                Return service.Length > 0 AndAlso userName.Length > 0
            End If

            Dim colonIndex As Integer = raw.IndexOf(":"c)
            If colonIndex > 0 AndAlso colonIndex < raw.Length - 1 Then
                service = CleanSegment(raw.Substring(0, colonIndex))
                userName = CleanSegment(raw.Substring(colonIndex + 1))
                Return service.Length > 0 AndAlso userName.Length > 0
            End If

            Return False
        End Function

        Private Shared Function GetStatusCode(ByVal ex As WebException) As HttpStatusCode?
            If ex Is Nothing Then Return Nothing

            Dim response As HttpWebResponse = TryCast(ex.Response, HttpWebResponse)
            If response Is Nothing Then Return Nothing

            Return response.StatusCode
        End Function

        Private Shared Function BuildTargetFilePath(ByVal media As IUserMedia, ByVal outputDir As String, ByVal index As Integer) As String
            Dim fileName As String = Path.GetFileName(media.File)
            If String.IsNullOrWhiteSpace(fileName) Then fileName = DeriveFileNameFromUrl(media.URL, index)

            fileName = SanitizeFileName(fileName)
            If String.IsNullOrWhiteSpace(Path.GetExtension(fileName)) Then
                fileName &= InferExtension(media.ContentType)
            End If

            Return Path.Combine(outputDir, fileName)
        End Function

        Private Shared Function DeriveFileNameFromUrl(ByVal url As String, ByVal index As Integer) As String
            If String.IsNullOrWhiteSpace(url) Then Return String.Format("media_{0}", index)

            Dim uri As Uri = Nothing
            If Uri.TryCreate(url, UriKind.Absolute, uri) Then
                Dim name As String = Path.GetFileName(uri.LocalPath)
                If Not String.IsNullOrWhiteSpace(name) Then Return name
            End If

            Return String.Format("media_{0}", index)
        End Function

        Private Shared Function SanitizeFileName(ByVal fileName As String) As String
            Dim clean As String = fileName
            For Each c As Char In Path.GetInvalidFileNameChars()
                clean = clean.Replace(c, "_"c)
            Next
            Return clean
        End Function

        Private Shared Function InferExtension(ByVal mediaType As UserMediaTypes) As String
            Select Case mediaType
                Case UserMediaTypes.Picture, UserMediaTypes.GIF
                    Return ".jpg"
                Case UserMediaTypes.Video, UserMediaTypes.VideoPre, UserMediaTypes.m3u8
                    Return ".mp4"
                Case UserMediaTypes.Audio, UserMediaTypes.AudioPre
                    Return ".mp3"
                Case UserMediaTypes.Text
                    Return ".txt"
                Case Else
                    Return ".bin"
            End Select
        End Function

        Private NotInheritable Class PluginWebClient
            Inherits WebClient

            Protected Overrides Function GetWebRequest(ByVal address As Uri) As WebRequest
                Dim request As WebRequest = MyBase.GetWebRequest(address)
                Dim httpRequest As HttpWebRequest = TryCast(request, HttpWebRequest)
                If httpRequest IsNot Nothing Then
                    httpRequest.AutomaticDecompression = DecompressionMethods.GZip Or DecompressionMethods.Deflate
                End If
                Return request
            End Function
        End Class

        Private Structure CreatorContext
            Public Service As String
            Public UserName As String
        End Structure

        Private NotInheritable Class CoomerPostsEnvelope
            Public Property results As List(Of CoomerPost)
        End Class

        Private NotInheritable Class CoomerProfile
            Public Property id As String
            Public Property name As String
            Public Property service As String
        End Class

        Private NotInheritable Class CoomerPost
            Public Property id As String
            Public Property user As String
            Public Property service As String
            Public Property title As String
            Public Property substring As String
            Public Property published As String
            Public Property file As CoomerFile
            Public Property attachments As List(Of CoomerFile)
        End Class

        Private NotInheritable Class CoomerFile
            Public Property name As String
            Public Property path As String
        End Class
    End Class
End Namespace

