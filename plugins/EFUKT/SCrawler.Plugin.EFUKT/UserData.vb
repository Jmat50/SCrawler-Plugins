Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports SCrawler.Plugin

Namespace EFUKTPlugin
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
                LogProvider?.Add("EFUKT: unable to resolve listing URL.")
                Exit Sub
            End If

            If IsVideoUrl(listingUrl) Then
                Dim singleMedia As IUserMedia = TryResolveVideoMedia(listingUrl, Token)
                If singleMedia IsNot Nothing Then
                    TempMediaList.Add(singleMedia)
                    If Not String.IsNullOrWhiteSpace(singleMedia.PostID) Then TempPostsList.Add(singleMedia.PostID)
                End If
                Exit Sub
            End If

            Dim seenPages As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim seenVideoUrls As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim currentPage As String = listingUrl
            Dim pageCount As Integer = 0
            Dim stopRequested As Boolean = False

            Do While Not stopRequested AndAlso Not String.IsNullOrWhiteSpace(currentPage)
                Token.ThrowIfCancellationRequested()
                Thrower?.ThrowAny()

                Dim normalizedPage As String = NormalizePageKey(currentPage)
                If Not seenPages.Add(normalizedPage) Then Exit Do
                pageCount += 1
                If pageCount > MaxListingPages Then Exit Do

                Dim html As String = Nothing
                Try
                    html = DownloadString(currentPage, "text/html,*/*")
                Catch ex As WebException
                    Dim status As HttpStatusCode? = GetStatusCode(ex)
                    If status.HasValue AndAlso status.Value = HttpStatusCode.NotFound Then
                        UserExists = False
                        LogProvider?.Add("EFUKT: listing page not found.")
                    Else
                        LogProvider?.Add(ex, String.Format("[EFUKT.GetMedia] failed to load listing page: {0}", currentPage))
                    End If
                    Exit Do
                Catch ex As Exception
                    LogProvider?.Add(ex, String.Format("[EFUKT.GetMedia] failed to load listing page: {0}", currentPage))
                    Exit Do
                End Try

                Dim videoUrls As List(Of String) = ExtractVideoUrls(html, currentPage)
                For Each videoUrl As String In videoUrls
                    Token.ThrowIfCancellationRequested()
                    Thrower?.ThrowAny()

                    If Not seenVideoUrls.Add(videoUrl) Then Continue For

                    Dim media As IUserMedia = TryResolveVideoMedia(videoUrl, Token)
                    If media Is Nothing Then Continue For
                    If Not IsPostDateAllowed(media.PostDate) Then Continue For

                    TempMediaList.Add(media)
                    If Not String.IsNullOrWhiteSpace(media.PostID) Then TempPostsList.Add(media.PostID)

                    If PostsNumberLimit.HasValue AndAlso TempPostsList.Count >= PostsNumberLimit.Value Then
                        stopRequested = True
                        Exit For
                    End If
                Next

                If stopRequested Then Exit Do
                currentPage = ExtractNextPageUrl(html, currentPage)
            Loop

            LogProvider?.Add(String.Format("EFUKT: parsed {0} video page(s), collected {1} media item(s).", TempPostsList.Count, TempMediaList.Count))
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
                    LogProvider?.Add(ex, String.Format("[EFUKT.Download] {0}", media.URL))
                End Try

                TempMediaList(i) = media
                RaiseEvent ProgressChanged(i + 1)
            Next
        End Sub

        Public Sub DownloadSingleObject(ByVal Data As IDownloadableMedia, ByVal Token As CancellationToken) Implements IPluginContentProvider.DownloadSingleObject
            If Data Is Nothing Then Exit Sub

            Dim media As IUserMedia = Nothing
            If IsVideoUrl(Data.URL) Then
                media = TryResolveVideoMedia(Data.URL, Token)
            End If

            If media Is Nothing Then
                media = New PluginUserMedia With {
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
            TempMediaList.Add(media)

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

        Private Function TryResolveVideoMedia(ByVal videoUrl As String, ByVal token As CancellationToken) As IUserMedia
            token.ThrowIfCancellationRequested()
            Thrower?.ThrowAny()

            Dim html As String = Nothing
            Try
                html = DownloadString(videoUrl, "text/html,*/*")
            Catch ex As Exception
                LogProvider?.Add(ex, String.Format("[EFUKT.Resolve] failed to load video page: {0}", videoUrl))
                Return Nothing
            End Try

            Dim directUrl As String = ExtractBestDirectVideoUrl(html)
            If String.IsNullOrWhiteSpace(directUrl) Then
                Return Nothing
            End If

            Dim videoId As String = ExtractVideoId(videoUrl)
            Dim postDate As Date? = ExtractUploadDate(html)
            Dim title As String = ExtractTitle(html)
            Dim qualityText As String = ExtractQualityTag(directUrl)

            Dim media As New PluginUserMedia With {
                .ContentType = UserMediaTypes.Video,
                .URL = directUrl,
                .URL_BASE = videoUrl,
                .File = BuildVideoFileName(videoId, qualityText, directUrl),
                .PostID = videoId,
                .PostDate = postDate,
                .PostText = title,
                .DownloadState = UserMediaStates.Unknown
            }

            Return media
        End Function

        Private Function ResolveListingUrl() As String
            Dim baseUrl As String = GetSiteBaseUrl()
            If String.IsNullOrWhiteSpace(baseUrl) Then Return String.Empty

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
                If HostMatchesDomain(absolute.Host, GetDomainOnly()) Then
                    Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
                End If
                Return String.Empty
            End If

            If trimmed.StartsWith("/", StringComparison.Ordinal) Then
                Return GetSiteBaseUrl() & trimmed
            End If

            If trimmed.IndexOf("/", StringComparison.Ordinal) >= 0 Then
                Return GetSiteBaseUrl() & "/" & trimmed.TrimStart("/"c)
            End If

            Dim pattern As String = DefaultListingUrlPattern
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData IsNot Nothing Then
                Dim p As String = CStr(settingsData.ListingUrlPattern.Value)
                If Not String.IsNullOrWhiteSpace(p) Then pattern = p
            End If

            Return String.Format(pattern, trimmed)
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

            Return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
        End Function

        Private Function ExtractVideoUrls(ByVal html As String, ByVal pageUrl As String) As List(Of String)
            Dim result As New List(Of String)
            If String.IsNullOrWhiteSpace(html) Then Return result

            Dim unique As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim baseUri As New Uri(pageUrl)

            For Each m As Match In Regex.Matches(html, "href=""(https?://(?:www\.)?efukt\.com/[0-9]+_[^""<>/]+\.html(?:\?[^""<>]*)?)""", RegexOptions.IgnoreCase)
                Dim absolute As String = m.Groups(1).Value
                If String.IsNullOrWhiteSpace(absolute) Then Continue For
                If unique.Add(absolute) Then result.Add(absolute)
            Next

            For Each m As Match In Regex.Matches(html, "href=""(/([0-9]+_[^""<>/]+\.html)(?:\?[^""<>]*)?)""", RegexOptions.IgnoreCase)
                Dim relative As String = m.Groups(1).Value
                If String.IsNullOrWhiteSpace(relative) Then Continue For

                Dim absolute As String = New Uri(baseUri, relative).AbsoluteUri
                If unique.Add(absolute) Then result.Add(absolute)
            Next

            Return result
        End Function

        Private Function ExtractNextPageUrl(ByVal html As String, ByVal currentPage As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim m As Match = Regex.Match(html, "<link[^>]*rel=""next""[^>]*href=""([^""<>]+)""", RegexOptions.IgnoreCase)
            If Not m.Success Then
                m = Regex.Match(html, "<a[^>]*href=""([^""<>]+)""[^>]*class=""[^""]*next_page[^""]*""", RegexOptions.IgnoreCase)
            End If
            If Not m.Success Then
                m = Regex.Match(html, "<a[^>]*class=""[^""]*next_page[^""]*""[^>]*href=""([^""<>]+)""", RegexOptions.IgnoreCase)
            End If
            If Not m.Success Then Return String.Empty

            Dim nextHref As String = WebUtility.HtmlDecode(m.Groups(1).Value)
            If String.IsNullOrWhiteSpace(nextHref) OrElse nextHref = "#" Then Return String.Empty

            Dim baseUri As New Uri(currentPage)
            Return New Uri(baseUri, nextHref).AbsoluteUri
        End Function

        Private Function ExtractBestDirectVideoUrl(ByVal html As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim urls As List(Of String) = ExtractDirectVideoUrls(html)
            If urls.Count = 0 Then Return String.Empty

            Return urls(0)
        End Function

        Private Shared Function ExtractDirectVideoUrls(ByVal html As String) As List(Of String)
            Dim result As New List(Of String)
            Dim unique As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each m As Match In Regex.Matches(html, "<source[^>]*src=""([^""<>]+\.mp4[^""<>]*)""", RegexOptions.IgnoreCase)
                Dim u As String = DecodeEncodedUrl(m.Groups(1).Value)
                If IsValidAbsoluteUrl(u) AndAlso unique.Add(u) Then result.Add(u)
            Next

            For Each m As Match In Regex.Matches(html, "https?://[^""'\s<]+\.mp4[^""'\s<]*", RegexOptions.IgnoreCase)
                Dim u As String = DecodeEncodedUrl(m.Value)
                If IsValidAbsoluteUrl(u) AndAlso unique.Add(u) Then result.Add(u)
            Next

            Return result
        End Function

        Private Shared Function ExtractQualityTag(ByVal url As String) As String
            Dim m As Match = Regex.Match(url, "([1-9][0-9]{2,3})p\.mp4", RegexOptions.IgnoreCase)
            If m.Success Then Return m.Groups(1).Value
            Return "source"
        End Function

        Private Shared Function ExtractVideoId(ByVal videoUrl As String) As String
            If String.IsNullOrWhiteSpace(videoUrl) Then Return String.Empty

            Dim m As Match = Regex.Match(videoUrl, "/([0-9]+)_[^/]+\.html", RegexOptions.IgnoreCase)
            If m.Success Then Return m.Groups(1).Value

            Return String.Empty
        End Function

        Private Shared Function ExtractUploadDate(ByVal html As String) As Date?
            If String.IsNullOrWhiteSpace(html) Then Return Nothing

            Dim m As Match = Regex.Match(html, "Uploaded\s+([0-9]{2}/[0-9]{2}/[0-9]{2})", RegexOptions.IgnoreCase)
            If Not m.Success Then Return Nothing

            Dim d As Date
            If Date.TryParse(m.Groups(1).Value, d) Then Return d
            Return Nothing
        End Function

        Private Shared Function ExtractTitle(ByVal html As String) As String
            If String.IsNullOrWhiteSpace(html) Then Return String.Empty

            Dim m As Match = Regex.Match(html, "<meta[^>]*property=""og:title""[^>]*content=""([^""<>]+)""", RegexOptions.IgnoreCase)
            If Not m.Success Then
                m = Regex.Match(html, "<meta[^>]*content=""([^""<>]+)""[^>]*property=""og:title""", RegexOptions.IgnoreCase)
            End If
            If m.Success Then Return WebUtility.HtmlDecode(m.Groups(1).Value).Trim()

            m = Regex.Match(html, "<title>([^<]+)</title>", RegexOptions.IgnoreCase)
            If m.Success Then
                Dim t As String = WebUtility.HtmlDecode(m.Groups(1).Value).Trim()
                t = Regex.Replace(t, "\|\s*eFukt\.com.*$", String.Empty, RegexOptions.IgnoreCase).Trim()
                Return t
            End If

            Return String.Empty
        End Function

        Private Shared Function DecodeEncodedUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim decoded As String = value.Replace("\/", "/")
            decoded = decoded.Replace("\u0026", "&")
            decoded = decoded.Replace("\\", "\")
            Return WebUtility.HtmlDecode(decoded)
        End Function

        Private Shared Function IsValidAbsoluteUrl(ByVal value As String) As Boolean
            Dim uri As Uri = Nothing
            Return Uri.TryCreate(value, UriKind.Absolute, uri)
        End Function

        Private Shared Function BuildVideoFileName(ByVal videoId As String, ByVal quality As String, ByVal directUrl As String) As String
            Dim idPart As String = If(String.IsNullOrWhiteSpace(videoId), "video", videoId)
            Dim qualityPart As String = If(String.IsNullOrWhiteSpace(quality), "source", quality)

            Dim fileName As String = String.Format("efukt_{0}_{1}.mp4", idPart, qualityPart)

            Dim uri As Uri = Nothing
            If Uri.TryCreate(directUrl, UriKind.Absolute, uri) Then
                Dim raw As String = Path.GetFileName(uri.LocalPath)
                If Not String.IsNullOrWhiteSpace(raw) Then fileName = raw
            End If

            Return SanitizeFileName(fileName)
        End Function

        Private Shared Function IsVideoUrl(ByVal value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then Return False

            Dim uri As Uri = Nothing
            If Uri.TryCreate(value, UriKind.Absolute, uri) Then
                Return Regex.IsMatch(uri.AbsolutePath, "^/[0-9]+_[^/]+\.html$", RegexOptions.IgnoreCase)
            End If

            Return Regex.IsMatch(value, "/[0-9]+_[^/]+\.html", RegexOptions.IgnoreCase)
        End Function

        Private Function ResolveOutputPath() As String
            If Not String.IsNullOrWhiteSpace(DataPath) Then Return DataPath
            Return Path.Combine(Environment.CurrentDirectory, "EFUKTDownloads")
        End Function

        Private Function GetSiteBaseUrl() As String
            Dim domainValue As String = DefaultDomain
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData IsNot Nothing Then
                Dim configured As String = CStr(settingsData.Domain.Value)
                If Not String.IsNullOrWhiteSpace(configured) Then domainValue = configured.Trim()
            End If

            If domainValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
               domainValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
                Return domainValue.TrimEnd("/"c)
            End If

            Return "https://" & domainValue.Trim().Trim("/"c)
        End Function

        Private Function GetDomainOnly() As String
            Dim baseUrl As String = GetSiteBaseUrl()
            Dim uri As Uri = Nothing
            If Uri.TryCreate(baseUrl, UriKind.Absolute, uri) Then Return uri.Host
            Return DefaultDomain
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

        Private Function IsPostDateAllowed(ByVal postDate As Date?) As Boolean
            If Not postDate.HasValue Then Return True

            If DownloadDateFrom.HasValue AndAlso postDate.Value.Date < DownloadDateFrom.Value.Date Then Return False
            If DownloadDateTo.HasValue AndAlso postDate.Value.Date > DownloadDateTo.Value.Date Then Return False

            Return True
        End Function

        Private Shared Function NormalizePageKey(ByVal pageUrl As String) As String
            If String.IsNullOrWhiteSpace(pageUrl) Then Return String.Empty

            Dim uri As Uri = Nothing
            If Uri.TryCreate(pageUrl, UriKind.Absolute, uri) Then
                Return uri.GetLeftPart(UriPartial.Path) & uri.Query
            End If

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
    End Class
End Namespace
