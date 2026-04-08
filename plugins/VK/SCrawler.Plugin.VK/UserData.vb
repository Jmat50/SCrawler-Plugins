
Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports SCrawler.Plugin

Namespace VKPlugin
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

        Private _listingPathFromExchange As String

        Public Sub New()
            ExistingContentList = New List(Of IUserMedia)
            TempPostsList = New List(Of String)
            TempMediaList = New List(Of IUserMedia)
            UserExists = True
            UserSuspended = False
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 Or SecurityProtocolType.Tls11 Or SecurityProtocolType.Tls
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
                If Not String.IsNullOrWhiteSpace(ex.Options) Then _listingPathFromExchange = ex.Options
            ElseIf TypeOf Obj Is String Then
                _listingPathFromExchange = CStr(Obj)
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

            Dim listingUrl As String = ResolveListingUrl()
            If String.IsNullOrWhiteSpace(listingUrl) Then
                UserExists = False
                LogProvider?.Add("VK: unable to resolve listing URL.")
                Exit Sub
            End If

            Dim seenPages As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim seenPosts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim seenMedia As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim pending As New Queue(Of String)
            pending.Enqueue(listingUrl)

            If IsDirectMediaUrl(listingUrl) Then
                AddMediaFromUrl(listingUrl, listingUrl, String.Empty, Nothing, seenPosts, seenMedia)
                Exit Sub
            End If

            Dim pagesParsed As Integer = 0
            While pending.Count > 0
                Token.ThrowIfCancellationRequested()
                Thrower?.ThrowAny()

                Dim pageUrl As String = pending.Dequeue()
                Dim pageKey As String = NormalizePageKey(pageUrl)
                If Not seenPages.Add(pageKey) Then Continue While

                pagesParsed += 1
                If pagesParsed > MaxListingPages Then Exit While

                Dim html As String = String.Empty
                Try
                    html = DownloadPageText(pageUrl)
                Catch ex As WebException
                    Dim status As HttpStatusCode? = GetStatusCode(ex)
                    If status.HasValue AndAlso status.Value = HttpStatusCode.NotFound Then
                        UserExists = False
                        LogProvider?.Add(String.Format("VK: page not found: {0}", pageUrl))
                    ElseIf status.HasValue AndAlso status.Value = HttpStatusCode.Gone Then
                        UserSuspended = True
                        LogProvider?.Add(String.Format("VK: page unavailable: {0}", pageUrl))
                    Else
                        LogProvider?.Add(ex, String.Format("[VK.GetMedia] failed to load page: {0}", pageUrl))
                    End If
                    Continue While
                Catch ex As Exception
                    LogProvider?.Add(ex, String.Format("[VK.GetMedia] failed to load page: {0}", pageUrl))
                    Continue While
                End Try

                Dim postDate As Date? = ExtractUploadDate(html)
                Dim pageTitle As String = ExtractTitle(html)

                For Each mediaUrl As String In ExtractDirectMediaUrls(html)
                    AddMediaFromUrl(mediaUrl, pageUrl, pageTitle, postDate, seenPosts, seenMedia)
                Next

                For Each postUrl As String In ExtractPostLinks(html, pageUrl)
                    Dim postKey As String = NormalizePageKey(postUrl)
                    If Not seenPages.Contains(postKey) Then pending.Enqueue(postUrl)
                Next

                Dim nextPage As String = ExtractNextPageUrl(html, pageUrl)
                If Not String.IsNullOrWhiteSpace(nextPage) Then
                    Dim nextKey As String = NormalizePageKey(nextPage)
                    If Not seenPages.Contains(nextKey) Then pending.Enqueue(nextPage)
                End If

                If PostsNumberLimit.HasValue AndAlso TempPostsList.Count >= PostsNumberLimit.Value Then Exit While
            End While

            LogProvider?.Add(String.Format("VK: parsed {0} page(s), collected {1} media item(s).", pagesParsed, TempMediaList.Count))
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
                            ApplyDownloadHeaders(wc, media.URL_BASE)
                            wc.DownloadFile(media.URL, targetFile)
                        End Using

                        media.File = targetFile
                        media.DownloadState = UserMediaStates.Downloaded
                    End If
                Catch ex As Exception
                    media.DownloadState = UserMediaStates.Missing
                    media.Attempts += 1
                    LogProvider?.Add(ex, String.Format("[VK.Download] {0}", media.URL))
                End Try

                TempMediaList(i) = media
                RaiseEvent ProgressChanged(i + 1)
            Next
        End Sub

        Public Sub DownloadSingleObject(ByVal Data As IDownloadableMedia, ByVal Token As CancellationToken) Implements IPluginContentProvider.DownloadSingleObject
            If Data Is Nothing Then Exit Sub

            Dim singleMedia As IUserMedia = Nothing
            If IsDirectMediaUrl(Data.URL) Then
                singleMedia = CreateMediaObject(Data.URL, Data.URL, Nothing, String.Empty)
            Else
                Try
                    Dim html As String = DownloadPageText(Data.URL)
                    Dim directMedia As List(Of String) = ExtractDirectMediaUrls(html)
                    If directMedia IsNot Nothing AndAlso directMedia.Count > 0 Then
                        singleMedia = CreateMediaObject(directMedia(0), Data.URL, ExtractUploadDate(html), ExtractTitle(html))
                    End If
                Catch ex As Exception
                    LogProvider?.Add(ex, String.Format("[VK.DownloadSingleObject] {0}", Data.URL))
                End Try
            End If

            If singleMedia Is Nothing Then
                singleMedia = New PluginUserMedia With {
                    .ContentType = Data.ContentType,
                    .URL = Data.URL,
                    .URL_BASE = Data.URL_BASE,
                    .File = Data.File,
                    .PostID = Data.PostID,
                    .PostDate = Data.PostDate,
                    .DownloadState = Data.DownloadState
                }
            End If

            TempMediaList.Clear()
            TempMediaList.Add(singleMedia)
            Download(Token)

            If TempMediaList.Count > 0 Then
                Dim result As IUserMedia = TempMediaList(0)
                Data.URL = result.URL
                Data.URL_BASE = result.URL_BASE
                Data.File = result.File
                Data.DownloadState = result.DownloadState
                Data.PostID = result.PostID
                Data.PostDate = result.PostDate
            End If
        End Sub

        Public Sub ResetHistoryData() Implements IPluginContentProvider.ResetHistoryData
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            ExistingContentList?.Clear()
            TempPostsList?.Clear()
            TempMediaList?.Clear()
        End Sub

        Private Sub AddMediaFromUrl(ByVal rawMediaUrl As String,
                                    ByVal sourcePageUrl As String,
                                    ByVal sourceTitle As String,
                                    ByVal postDate As Date?,
                                    ByVal seenPosts As HashSet(Of String),
                                    ByVal seenMedia As HashSet(Of String))
            Dim media As IUserMedia = CreateMediaObject(rawMediaUrl, sourcePageUrl, postDate, sourceTitle)
            If media Is Nothing Then Exit Sub

            Dim mediaKey As String = NormalizePageKey(media.URL)
            If String.IsNullOrWhiteSpace(mediaKey) Then mediaKey = media.URL
            If Not seenMedia.Add(mediaKey) Then Exit Sub

            If Not String.IsNullOrWhiteSpace(media.PostID) Then
                If seenPosts.Add(media.PostID) Then TempPostsList.Add(media.PostID)
            End If

            If Not IsPostDateAllowed(media.PostDate) Then Exit Sub
            TempMediaList.Add(media)
        End Sub

        Private Function CreateMediaObject(ByVal rawMediaUrl As String,
                                           ByVal sourcePageUrl As String,
                                           ByVal postDate As Date?,
                                           ByVal sourceTitle As String) As IUserMedia
            Dim directUrl As String = NormalizeDirectMediaUrl(rawMediaUrl)
            If String.IsNullOrWhiteSpace(directUrl) Then Return Nothing

            Dim postId As String = ExtractPostIdFromUrl(sourcePageUrl)
            If String.IsNullOrWhiteSpace(postId) Then postId = ExtractPostIdFromUrl(directUrl)

            Dim mediaType As UserMediaTypes = InferContentTypeFromUrl(directUrl)
            Dim fileName As String = BuildMediaFileName(directUrl, postId, mediaType)

            Return New PluginUserMedia With {
                .ContentType = mediaType,
                .URL = directUrl,
                .URL_BASE = sourcePageUrl,
                .File = fileName,
                .PostID = postId,
                .PostDate = postDate,
                .PostText = sourceTitle,
                .DownloadState = UserMediaStates.Unknown
            }
        End Function

        Private Function ResolveListingUrl() As String
            Dim candidates As String() = {
                Options,
                _listingPathFromExchange,
                NameTrue,
                Name,
                ID
            }

            For Each candidate As String In candidates
                Dim resolved As String = NormalizeToAbsoluteUrl(candidate)
                If Not String.IsNullOrWhiteSpace(resolved) Then Return resolved
            Next

            Return String.Empty
        End Function

        Private Function NormalizeToAbsoluteUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim trimmed As String = value.Trim()
            Dim absolute As Uri = Nothing
            If Uri.TryCreate(trimmed, UriKind.Absolute, absolute) Then
                If HostMatchesDomain(absolute.Host, GetDomainOnly()) OrElse IsKnownMediaHost(absolute.Host) Then
                    Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
                End If
                Return String.Empty
            End If

            If trimmed.StartsWith("/", StringComparison.Ordinal) Then
                Return GetSiteBaseUrl() & trimmed
            End If

            If Regex.IsMatch(trimmed, "^(?:id\d+|club\d+|public\d+|[A-Za-z0-9_.-]{2,})$", RegexOptions.IgnoreCase) Then
                Dim pattern As String = DefaultListingUrlPattern
                Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
                If settingsData IsNot Nothing Then
                    Dim p As String = CStr(settingsData.ListingUrlPattern.Value)
                    If Not String.IsNullOrWhiteSpace(p) Then pattern = p
                End If
                Return String.Format(pattern, trimmed)
            End If

            If trimmed.IndexOf("/"c) >= 0 Then
                Return GetSiteBaseUrl() & "/" & trimmed.TrimStart("/"c)
            End If

            Return String.Empty
        End Function

        Private Function DownloadPageText(ByVal url As String) As String
            Try
                Return DownloadString(url, "text/html,application/xhtml+xml,*/*")
            Catch ex As WebException
                Dim status As HttpStatusCode? = GetStatusCode(ex)
                If status.HasValue AndAlso (status.Value = HttpStatusCode.Forbidden OrElse
                                            status.Value = CType(429, HttpStatusCode) OrElse
                                            status.Value = HttpStatusCode.ServiceUnavailable) Then
                    Dim fallbackUrl As String = BuildFallbackMirrorUrl(url)
                    LogProvider?.Add(String.Format("VK: using fallback mirror for {0}", url))
                    Return DownloadString(fallbackUrl, "text/plain,*/*")
                End If
                Throw
            End Try
        End Function

        Private Function DownloadString(ByVal url As String, ByVal accept As String) As String
            Using wc As WebClient = CreateWebClient(accept, url)
                Return wc.DownloadString(url)
            End Using
        End Function

        Private Function CreateWebClient(ByVal accept As String, ByVal referer As String) As WebClient
            Dim wc As New WebClient With {
                .Encoding = Encoding.UTF8
            }

            wc.Headers(HttpRequestHeader.UserAgent) = ResolveUserAgent()
            If Not String.IsNullOrWhiteSpace(accept) Then wc.Headers(HttpRequestHeader.Accept) = accept
            If Not String.IsNullOrWhiteSpace(referer) Then wc.Headers(HttpRequestHeader.Referer) = referer

            Return wc
        End Function

        Private Sub ApplyDownloadHeaders(ByVal wc As WebClient, ByVal referer As String)
            wc.Headers(HttpRequestHeader.UserAgent) = ResolveUserAgent()
            If Not String.IsNullOrWhiteSpace(referer) Then wc.Headers(HttpRequestHeader.Referer) = referer
        End Sub

        Private Function ResolveUserAgent() As String
            If Settings IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(Settings.UserAgentDefault) Then
                Return Settings.UserAgentDefault
            End If

            Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        End Function

        Private Shared Function BuildFallbackMirrorUrl(ByVal url As String) As String
            If String.IsNullOrWhiteSpace(url) Then Return String.Empty

            If url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                Return FallbackMirrorPrefix & url.Substring(8)
            End If

            If url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then
                Return FallbackMirrorPrefix & url.Substring(7)
            End If

            Return FallbackMirrorPrefix & url
        End Function

        Private Function ExtractPostLinks(ByVal html As String, ByVal pageUrl As String) As List(Of String)
            Dim result As New List(Of String)
            If String.IsNullOrWhiteSpace(html) Then Return result

            Dim baseUri As New Uri(pageUrl)
            Dim unique As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim decoded As String = DecodeEscapedText(html)

            Dim relativePattern As String = "(?:(?:href|data-href|data-url)\s*=\s*[""'])(/(?:wall|photo|video|clip|doc|album)-?\d+_\d+(?:\?[^""'<>\s]*)?)"
            For Each m As Match In Regex.Matches(decoded, relativePattern, RegexOptions.IgnoreCase)
                Dim relative As String = m.Groups(1).Value
                If String.IsNullOrWhiteSpace(relative) Then Continue For

                Dim absoluteLink As String = New Uri(baseUri, relative).AbsoluteUri
                If IsPostLikeUrl(absoluteLink) AndAlso unique.Add(absoluteLink) Then result.Add(absoluteLink)
            Next

            Dim absolutePattern As String = "https?://(?:m\.)?vk\.com/(?:wall|photo|video|clip|doc|album)-?\d+_\d+(?:\?[^""'<>\s]*)?"
            For Each m As Match In Regex.Matches(decoded, absolutePattern, RegexOptions.IgnoreCase)
                Dim absoluteLink As String = m.Value
                If String.IsNullOrWhiteSpace(absoluteLink) Then Continue For

                If IsPostLikeUrl(absoluteLink) AndAlso unique.Add(absoluteLink) Then result.Add(absoluteLink)
            Next

            Return result
        End Function

        Private Function ExtractNextPageUrl(ByVal html As String, ByVal currentPage As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim m As Match = Regex.Match(html, "<link[^>]*rel=""next""[^>]*href=""([^""<>]+)""", RegexOptions.IgnoreCase)
            If Not m.Success Then
                m = Regex.Match(html, "href=""([^""<>]*(?:offset|from)=\d+[^""<>]*)""", RegexOptions.IgnoreCase)
            End If
            If Not m.Success Then Return String.Empty

            Dim href As String = WebUtility.HtmlDecode(m.Groups(1).Value)
            If String.IsNullOrWhiteSpace(href) OrElse href = "#" Then Return String.Empty

            Dim baseUri As New Uri(currentPage)
            Return New Uri(baseUri, href).AbsoluteUri
        End Function

        Private Function ExtractDirectMediaUrls(ByVal html As String) As List(Of String)
            Dim result As New List(Of String)
            If String.IsNullOrWhiteSpace(html) Then Return result

            Dim unique As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim decoded As String = DecodeEscapedText(html)
            decoded = decoded.Replace("&amp;", "&")

            Dim pattern As String = "(?:https?:)?//[A-Za-z0-9\-._%/\?&=:+#~]+?\.(?:jpg|jpeg|png|gif|webp|bmp|mp4|webm|mov|m4v|mkv|avi|mp3|m4a|ogg|wav|flac|aac)(?:\?[^""'\s<>)]+)?"
            For Each m As Match In Regex.Matches(decoded, pattern, RegexOptions.IgnoreCase)
                Dim raw As String = m.Value
                If String.IsNullOrWhiteSpace(raw) Then Continue For

                Dim normalized As String = NormalizeDirectMediaUrl(raw)
                If String.IsNullOrWhiteSpace(normalized) Then Continue For

                If unique.Add(normalized) Then result.Add(normalized)
            Next

            Return result
        End Function

        Private Function NormalizeDirectMediaUrl(ByVal raw As String) As String
            If String.IsNullOrWhiteSpace(raw) Then Return String.Empty

            Dim decoded As String = DecodeEscapedText(raw).Trim()
            If decoded.StartsWith("//", StringComparison.Ordinal) Then decoded = "https:" & decoded

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(decoded, UriKind.Absolute, uri) Then Return String.Empty

            If Not IsKnownMediaHost(uri.Host) AndAlso Not HostMatchesDomain(uri.Host, GetDomainOnly()) Then Return String.Empty
            If Not Regex.IsMatch(uri.AbsolutePath, "\.(?:jpg|jpeg|png|gif|webp|bmp|mp4|webm|mov|m4v|mkv|avi|mp3|m4a|ogg|wav|flac|aac)$", RegexOptions.IgnoreCase) Then Return String.Empty

            Return uri.GetLeftPart(UriPartial.Path) & uri.Query
        End Function

        Private Shared Function DecodeEscapedText(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim decoded As String = value.Replace("\\/", "/")
            decoded = decoded.Replace("\u0026", "&")
            decoded = decoded.Replace("\\", "")
            Return WebUtility.HtmlDecode(decoded)
        End Function

        Private Shared Function ExtractTitle(ByVal html As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim m As Match = Regex.Match(html, "<meta[^>]*property=""og:title""[^>]*content=""([^""<>]+)""", RegexOptions.IgnoreCase)
            If Not m.Success Then
                m = Regex.Match(html, "<meta[^>]*content=""([^""<>]+)""[^>]*property=""og:title""", RegexOptions.IgnoreCase)
            End If
            If m.Success Then Return WebUtility.HtmlDecode(m.Groups(1).Value).Trim()

            m = Regex.Match(html, "<title>([^<]+)</title>", RegexOptions.IgnoreCase)
            If m.Success Then Return WebUtility.HtmlDecode(m.Groups(1).Value).Trim()

            Return String.Empty
        End Function

        Private Shared Function ExtractUploadDate(ByVal html As String) As Date?
            If String.IsNullOrWhiteSpace(html) Then Return Nothing

            Dim m As Match = Regex.Match(html, """(?:datePublished|uploadDate|published|created_at)""\s*:\s*""([^""\n]+)""", RegexOptions.IgnoreCase)
            If m.Success Then
                Dim parsed As Date
                If Date.TryParse(m.Groups(1).Value, parsed) Then Return parsed
            End If

            m = Regex.Match(html, """date""\s*:\s*(\d{10,13})", RegexOptions.IgnoreCase)
            If m.Success Then
                Dim unixRaw As Long
                If Long.TryParse(m.Groups(1).Value, unixRaw) Then
                    If unixRaw > 9999999999L Then unixRaw = unixRaw \ 1000
                    Try
                        Return DateTimeOffset.FromUnixTimeSeconds(unixRaw).UtcDateTime
                    Catch
                    End Try
                End If
            End If

            Return Nothing
        End Function

        Private Shared Function ExtractPostIdFromUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim uri As Uri = Nothing
            If Uri.TryCreate(value, UriKind.Absolute, uri) Then
                Dim mPath As Match = Regex.Match(uri.AbsolutePath, "^/((?:wall|photo|video|clip|doc|album)-?\d+_\d+)/?$", RegexOptions.IgnoreCase)
                If mPath.Success Then Return mPath.Groups(1).Value

                Dim mQuery As Match = Regex.Match(uri.Query, "(?:^|[?&])z=(?:photo|video|wall)(-?\d+_\d+)", RegexOptions.IgnoreCase)
                If mQuery.Success Then Return mQuery.Groups(1).Value
            End If

            Dim m As Match = Regex.Match(value, "((?:wall|photo|video|clip|doc|album)-?\d+_\d+)", RegexOptions.IgnoreCase)
            If m.Success Then Return m.Groups(1).Value

            Return String.Empty
        End Function

        Private Shared Function InferContentTypeFromUrl(ByVal url As String) As UserMediaTypes
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return UserMediaTypes.Undefined

            Select Case Path.GetExtension(uri.LocalPath).ToLowerInvariant()
                Case ".jpg", ".jpeg", ".png", ".webp", ".bmp"
                    Return UserMediaTypes.Picture
                Case ".gif"
                    Return UserMediaTypes.GIF
                Case ".mp4", ".webm", ".mov", ".m4v", ".mkv", ".avi"
                    Return UserMediaTypes.Video
                Case ".mp3", ".m4a", ".ogg", ".wav", ".flac", ".aac"
                    Return UserMediaTypes.Audio
                Case Else
                    Return UserMediaTypes.Undefined
            End Select
        End Function

        Private Shared Function BuildMediaFileName(ByVal directUrl As String, ByVal postId As String, ByVal mediaType As UserMediaTypes) As String
            Dim fileName As String = String.Empty
            Dim uri As Uri = Nothing
            If Uri.TryCreate(directUrl, UriKind.Absolute, uri) Then
                fileName = Path.GetFileName(uri.LocalPath)
            End If

            If String.IsNullOrWhiteSpace(fileName) Then
                Dim idPart As String = If(String.IsNullOrWhiteSpace(postId), "item", postId)
                fileName = "vk_" & idPart & InferExtension(mediaType)
            End If

            fileName = SanitizeFileName(fileName)
            If String.IsNullOrWhiteSpace(Path.GetExtension(fileName)) Then
                fileName &= InferExtension(mediaType)
            End If

            Return fileName
        End Function

        Private Function IsDirectMediaUrl(ByVal value As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(NormalizeDirectMediaUrl(value))
        End Function

        Private Shared Function IsPostLikePath(ByVal path As String) As Boolean
            If String.IsNullOrWhiteSpace(path) Then Return False
            Return Regex.IsMatch(path, "^/(?:wall|photo|video|clip|doc|album)-?\d+_\d+/?$", RegexOptions.IgnoreCase)
        End Function

        Private Function IsPostLikeUrl(ByVal value As String) As Boolean
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(value, UriKind.Absolute, uri) Then Return False
            If Not HostMatchesDomain(uri.Host, GetDomainOnly()) Then Return False
            Return IsPostLikePath(uri.AbsolutePath)
        End Function

        Private Function ResolveOutputPath() As String
            If Not String.IsNullOrWhiteSpace(DataPath) Then Return DataPath
            Return Path.Combine(Environment.CurrentDirectory, "VKDownloads")
        End Function

        Private Function GetSiteBaseUrl() As String
            Return "https://" & GetDomainOnly()
        End Function

        Private Function GetDomainOnly() As String
            Dim domainValue As String = DefaultDomain
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData IsNot Nothing Then
                Dim configured As String = CStr(settingsData.Domain.Value)
                If Not String.IsNullOrWhiteSpace(configured) Then domainValue = configured.Trim()
            End If

            domainValue = domainValue.Trim().ToLowerInvariant()
            If domainValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then domainValue = domainValue.Substring(7)
            If domainValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then domainValue = domainValue.Substring(8)
            Return domainValue.Trim("/"c)
        End Function

        Private Shared Function HostMatchesDomain(ByVal host As String, ByVal configuredDomain As String) As Boolean
            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(configuredDomain) Then Return False

            Dim h As String = host.ToLowerInvariant()
            Dim d As String = configuredDomain.ToLowerInvariant()

            If h.Equals(d, StringComparison.OrdinalIgnoreCase) Then Return True
            If h.EndsWith("." & d, StringComparison.OrdinalIgnoreCase) Then Return True

            If d.StartsWith("www.", StringComparison.OrdinalIgnoreCase) Then
                Dim noWww As String = d.Substring(4)
                If h.Equals(noWww, StringComparison.OrdinalIgnoreCase) Then Return True
                If h.EndsWith("." & noWww, StringComparison.OrdinalIgnoreCase) Then Return True
            End If

            Return False
        End Function

        Private Shared Function IsKnownMediaHost(ByVal host As String) As Boolean
            If String.IsNullOrWhiteSpace(host) Then Return False
            Dim h As String = host.ToLowerInvariant()
            Return h.Contains("userapi.com") OrElse h.Contains("vk-cdn.net") OrElse h.Contains("vkuseraudio.net")
        End Function

        Private Function IsPostDateAllowed(ByVal postDate As Date?) As Boolean
            If Not postDate.HasValue Then Return True
            If DownloadDateFrom.HasValue AndAlso postDate.Value.Date < DownloadDateFrom.Value.Date Then Return False
            If DownloadDateTo.HasValue AndAlso postDate.Value.Date > DownloadDateTo.Value.Date Then Return False
            Return True
        End Function

        Private Shared Function NormalizePageKey(ByVal pageUrl As String) As String
            If String.IsNullOrWhiteSpace(pageUrl) Then Return String.Empty
            Dim uri As Uri = Nothing
            If Uri.TryCreate(pageUrl, UriKind.Absolute, uri) Then Return uri.GetLeftPart(UriPartial.Path) & uri.Query
            Return pageUrl.Trim()
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
            If String.IsNullOrWhiteSpace(Path.GetExtension(fileName)) Then fileName &= InferExtension(media.ContentType)
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
                Case UserMediaTypes.Picture
                    Return ".jpg"
                Case UserMediaTypes.GIF
                    Return ".gif"
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
    End Class
End Namespace
