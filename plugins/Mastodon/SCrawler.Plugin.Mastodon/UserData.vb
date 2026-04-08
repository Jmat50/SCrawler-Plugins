Imports System.Collections.Generic
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Web.Script.Serialization
Imports SCrawler.Plugin

Namespace MastodonPlugin
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
        Private _accountFromExchange As String

        Private Const JsonAccept As String = "application/json,text/plain,*/*"

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
                If Not String.IsNullOrWhiteSpace(ex.UserName) Then _accountFromExchange = ex.UserName
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

            Dim target As ResolvedTarget = ResolveTarget()
            If target.Kind = TargetKind.None Then
                UserExists = False
                LogProvider?.Add("Mastodon: unable to resolve user/profile/status from input.")
                Exit Sub
            End If

            Dim seenPosts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim seenMediaUrls As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            Select Case target.Kind
                Case TargetKind.DirectMedia
                    Dim singleMedia As IUserMedia = CreateDirectMedia(target.AbsoluteUrl, target.AbsoluteUrl)
                    TempMediaList.Add(singleMedia)
                    If Not String.IsNullOrWhiteSpace(singleMedia.PostID) Then TempPostsList.Add(singleMedia.PostID)

                Case TargetKind.Status
                    Try
                        Token.ThrowIfCancellationRequested()
                        Thrower?.ThrowAny()

                        Dim status As MastodonStatus = FetchStatus(target.Host, target.StatusID, Token)
                        If status Is Nothing Then
                            UserExists = False
                            Exit Select
                        End If

                        Dim postDate As Date? = ParsePostDate(status.created_at)
                        If Not IsPostDateAllowed(postDate) Then Exit Select

                        Dim postId As String = status.id
                        If String.IsNullOrWhiteSpace(postId) Then postId = target.StatusID
                        If Not String.IsNullOrWhiteSpace(postId) AndAlso seenPosts.Add(postId) Then
                            TempPostsList.Add(postId)
                        End If

                        AddStatusMedia(status, target.Host, target.AbsoluteUrl, postDate, seenMediaUrls)
                    Catch ex As WebException
                        HandleRequestFailure(ex, String.Format("[Mastodon.GetMedia] status request failed ({0})", target.AbsoluteUrl))
                    Catch ex As Exception
                        LogProvider?.Add(ex, String.Format("[Mastodon.GetMedia] status request failed ({0})", target.AbsoluteUrl))
                    End Try

                Case TargetKind.Profile
                    DownloadProfileMedia(target, seenPosts, seenMediaUrls, Token)
            End Select

            LogProvider?.Add(String.Format("Mastodon: parsed {0} post(s), collected {1} media item(s).", TempPostsList.Count, TempMediaList.Count))
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
                    LogProvider?.Add(ex, String.Format("[Mastodon.Download] {0}", media.URL))
                End Try

                TempMediaList(i) = media
                RaiseEvent ProgressChanged(i + 1)
            Next
        End Sub

        Public Sub DownloadSingleObject(ByVal Data As IDownloadableMedia, ByVal Token As CancellationToken) Implements IPluginContentProvider.DownloadSingleObject
            If Data Is Nothing Then Exit Sub

            Dim media As IUserMedia = Nothing

            Try
                Dim target As ResolvedTarget = ResolveTargetFromValue(Data.URL)
                If target.Kind = TargetKind.DirectMedia Then
                    media = CreateDirectMedia(target.AbsoluteUrl, target.AbsoluteUrl)
                ElseIf target.Kind = TargetKind.Status Then
                    Dim status As MastodonStatus = FetchStatus(target.Host, target.StatusID, Token)
                    If status IsNot Nothing Then
                        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                        TempMediaList.Clear()
                        AddStatusMedia(status, target.Host, target.AbsoluteUrl, ParsePostDate(status.created_at), seen)
                        If TempMediaList.Count > 0 Then
                            media = TempMediaList(0)
                        End If
                    End If
                End If
            Catch ex As Exception
                LogProvider?.Add(ex, "[Mastodon.DownloadSingleObject] unable to resolve media from URL.")
            End Try

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

        Private Sub DownloadProfileMedia(ByVal target As ResolvedTarget,
                                         ByVal seenPosts As HashSet(Of String),
                                         ByVal seenMediaUrls As HashSet(Of String),
                                         ByVal token As CancellationToken)
            Dim account As MastodonAccount = Nothing

            Try
                account = LookupAccount(target.Host, target.AccountLookup, token)
            Catch ex As WebException
                HandleRequestFailure(ex, String.Format("[Mastodon.GetMedia] account lookup failed ({0})", target.AccountLookup))
                Exit Sub
            Catch ex As Exception
                LogProvider?.Add(ex, String.Format("[Mastodon.GetMedia] account lookup failed ({0})", target.AccountLookup))
                Exit Sub
            End Try

            If account Is Nothing OrElse String.IsNullOrWhiteSpace(account.id) Then
                UserExists = False
                LogProvider?.Add("Mastodon: account lookup did not return a valid account id.")
                Exit Sub
            End If

            If String.IsNullOrWhiteSpace(NameTrue) Then NameTrue = account.username
            If String.IsNullOrWhiteSpace(Name) Then Name = If(String.IsNullOrWhiteSpace(account.display_name), account.acct, account.display_name)
            If String.IsNullOrWhiteSpace(ID) Then ID = account.id
            If String.IsNullOrWhiteSpace(UserDescription) Then UserDescription = StripHtml(account.note)

            Dim maxId As String = String.Empty
            Dim pageCount As Integer = 0
            Dim stopRequested As Boolean = False

            Do While Not stopRequested
                token.ThrowIfCancellationRequested()
                Thrower?.ThrowAny()

                pageCount += 1
                If pageCount > MaxListingPages Then Exit Do

                Dim statuses As List(Of MastodonStatus) = Nothing

                Try
                    statuses = GetAccountStatuses(target.Host, account.id, maxId, token)
                Catch ex As WebException
                    HandleRequestFailure(ex, String.Format("[Mastodon.GetMedia] statuses request failed ({0})", target.Host))
                    Exit Do
                Catch ex As Exception
                    LogProvider?.Add(ex, String.Format("[Mastodon.GetMedia] statuses request failed ({0})", target.Host))
                    Exit Do
                End Try

                If statuses Is Nothing OrElse statuses.Count = 0 Then Exit Do

                Dim lastPostId As String = String.Empty
                For Each status As MastodonStatus In statuses
                    token.ThrowIfCancellationRequested()
                    Thrower?.ThrowAny()

                    If status Is Nothing OrElse String.IsNullOrWhiteSpace(status.id) Then Continue For
                    lastPostId = status.id

                    If Not seenPosts.Add(status.id) Then Continue For

                    Dim postDate As Date? = ParsePostDate(status.created_at)
                    If Not IsPostDateAllowed(postDate) Then Continue For

                    TempPostsList.Add(status.id)
                    AddStatusMedia(status, target.Host, target.AbsoluteUrl, postDate, seenMediaUrls)

                    If PostsNumberLimit.HasValue AndAlso TempPostsList.Count >= PostsNumberLimit.Value Then
                        stopRequested = True
                        Exit For
                    End If
                Next

                If stopRequested Then Exit Do
                If String.IsNullOrWhiteSpace(lastPostId) Then Exit Do

                maxId = lastPostId
                If statuses.Count < DefaultStatusesLimit Then Exit Do
            Loop
        End Sub

        Private Sub AddStatusMedia(ByVal status As MastodonStatus,
                                   ByVal host As String,
                                   ByVal fallbackUrl As String,
                                   ByVal explicitPostDate As Date?,
                                   ByVal seenMediaUrls As HashSet(Of String))
            Dim source As MastodonStatus = status
            Dim attachments As List(Of MastodonMediaAttachment) = status.media_attachments

            If (attachments Is Nothing OrElse attachments.Count = 0) AndAlso status.reblog IsNot Nothing Then
                source = status.reblog
                attachments = source.media_attachments
            End If

            If attachments Is Nothing OrElse attachments.Count = 0 Then Exit Sub

            Dim postId As String = status.id
            If String.IsNullOrWhiteSpace(postId) Then postId = source.id
            If String.IsNullOrWhiteSpace(postId) Then postId = "status"

            Dim postDate As Date? = explicitPostDate
            If Not postDate.HasValue Then
                postDate = ParsePostDate(status.created_at)
                If Not postDate.HasValue Then postDate = ParsePostDate(source.created_at)
            End If

            Dim postText As String = StripHtml(FirstNotEmpty(status.content, source.content))
            Dim postUrl As String = FirstNotEmpty(status.url, source.url, fallbackUrl)
            If String.IsNullOrWhiteSpace(postUrl) Then postUrl = BuildStatusPageUrl(host, postId)

            Dim mediaOrdinal As Integer = 0
            For Each media As MastodonMediaAttachment In attachments
                mediaOrdinal += 1
                If media Is Nothing Then Continue For

                Dim directUrl As String = FirstNotEmpty(media.url, media.remote_url, media.preview_url)
                If String.IsNullOrWhiteSpace(directUrl) Then Continue For

                Dim mediaKey As String = NormalizeMediaKey(directUrl)
                If Not seenMediaUrls.Add(mediaKey) Then Continue For

                Dim mediaId As String = media.id
                If String.IsNullOrWhiteSpace(mediaId) Then mediaId = mediaOrdinal.ToString()

                Dim mediaType As UserMediaTypes = InferMediaType(media.type, directUrl)
                Dim fileName As String = BuildMediaFileName(directUrl, postId, mediaId, mediaType)

                TempMediaList.Add(New PluginUserMedia With {
                    .ContentType = mediaType,
                    .URL = directUrl,
                    .URL_BASE = postUrl,
                    .File = fileName,
                    .PostID = postId,
                    .PostDate = postDate,
                    .PostText = postText,
                    .DownloadState = UserMediaStates.Unknown
                })
            Next
        End Sub

        Private Function LookupAccount(ByVal host As String, ByVal accountLookup As String, ByVal token As CancellationToken) As MastodonAccount
            token.ThrowIfCancellationRequested()
            Thrower?.ThrowAny()

            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(accountLookup) Then Return Nothing

            Dim url As String = String.Format("https://{0}/api/v1/accounts/lookup?acct={1}",
                                              host,
                                              Uri.EscapeDataString(accountLookup))

            Dim json As String = DownloadJson(url, url)
            If String.IsNullOrWhiteSpace(json) Then Return Nothing

            Return CreateSerializer().Deserialize(Of MastodonAccount)(json)
        End Function

        Private Function GetAccountStatuses(ByVal host As String, ByVal accountId As String, ByVal maxId As String, ByVal token As CancellationToken) As List(Of MastodonStatus)
            token.ThrowIfCancellationRequested()
            Thrower?.ThrowAny()

            Dim url As String = String.Format("https://{0}/api/v1/accounts/{1}/statuses?only_media=true&exclude_replies=true&limit={2}",
                                              host,
                                              accountId,
                                              DefaultStatusesLimit)
            If Not String.IsNullOrWhiteSpace(maxId) Then
                url &= "&max_id=" & Uri.EscapeDataString(maxId)
            End If

            Dim json As String = DownloadJson(url, url)
            If String.IsNullOrWhiteSpace(json) Then Return New List(Of MastodonStatus)

            Dim result As List(Of MastodonStatus) = CreateSerializer().Deserialize(Of List(Of MastodonStatus))(json)
            If result Is Nothing Then Return New List(Of MastodonStatus)
            Return result
        End Function

        Private Function FetchStatus(ByVal host As String, ByVal statusId As String, ByVal token As CancellationToken) As MastodonStatus
            token.ThrowIfCancellationRequested()
            Thrower?.ThrowAny()

            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(statusId) Then Return Nothing

            Dim url As String = String.Format("https://{0}/api/v1/statuses/{1}", host, statusId)
            Dim json As String = DownloadJson(url, url)
            If String.IsNullOrWhiteSpace(json) Then Return Nothing

            Return CreateSerializer().Deserialize(Of MastodonStatus)(json)
        End Function

        Private Function DownloadJson(ByVal url As String, ByVal referer As String) As String
            Using wc As WebClient = CreateWebClient(JsonAccept, referer)
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

        Private Function ResolveTarget() As ResolvedTarget
            Dim candidates As String() = {
                Options,
                _listingPathFromExchange,
                NameTrue,
                Name,
                _accountFromExchange,
                ID
            }

            For Each candidate As String In candidates
                Dim resolved As ResolvedTarget = ResolveTargetFromValue(candidate)
                If resolved.Kind <> TargetKind.None Then Return resolved
            Next

            Return New ResolvedTarget With {.Kind = TargetKind.None}
        End Function

        Private Function ResolveTargetFromValue(ByVal value As String) As ResolvedTarget
            If String.IsNullOrWhiteSpace(value) Then
                Return New ResolvedTarget With {.Kind = TargetKind.None}
            End If

            Dim absoluteValue As String = NormalizeToAbsoluteUrl(value)
            If String.IsNullOrWhiteSpace(absoluteValue) Then
                Return New ResolvedTarget With {.Kind = TargetKind.None}
            End If

            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(absoluteValue, UriKind.Absolute, uri) Then
                Return New ResolvedTarget With {.Kind = TargetKind.None}
            End If

            If IsDirectMediaPath(uri.AbsolutePath) Then
                Return New ResolvedTarget With {
                    .Kind = TargetKind.DirectMedia,
                    .AbsoluteUrl = uri.AbsoluteUri,
                    .Host = uri.Host
                }
            End If

            Dim statusId As String = String.Empty
            If TryExtractStatusId(uri.AbsolutePath, statusId) Then
                Return New ResolvedTarget With {
                    .Kind = TargetKind.Status,
                    .AbsoluteUrl = uri.AbsoluteUri,
                    .Host = uri.Host,
                    .StatusID = statusId
                }
            End If

            Dim profileToken As String = String.Empty
            If TryExtractProfileToken(uri.AbsolutePath, profileToken) Then
                Dim accountLookup As String = BuildLookupAcct(profileToken, uri.Host)
                If Not String.IsNullOrWhiteSpace(accountLookup) Then
                    Return New ResolvedTarget With {
                        .Kind = TargetKind.Profile,
                        .AbsoluteUrl = uri.AbsoluteUri,
                        .Host = uri.Host,
                        .AccountLookup = accountLookup
                    }
                End If
            End If

            Return New ResolvedTarget With {.Kind = TargetKind.None}
        End Function

        Private Function NormalizeToAbsoluteUrl(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim trimmed As String = value.Trim()
            Dim absolute As Uri = Nothing

            If Uri.TryCreate(trimmed, UriKind.Absolute, absolute) Then
                Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
            End If

            If Not trimmed.Contains("://") AndAlso trimmed.IndexOf("/"c) > 0 Then
                Dim firstPart As String = trimmed.Substring(0, trimmed.IndexOf("/"c))
                If firstPart.Contains(".") Then
                    Dim prefixed As String = "https://" & trimmed.TrimStart("/"c)
                    If Uri.TryCreate(prefixed, UriKind.Absolute, absolute) Then
                        Return absolute.GetLeftPart(UriPartial.Path) & absolute.Query
                    End If
                End If
            End If

            Dim userOnDomain As Match = Regex.Match(trimmed, "^@?([A-Za-z0-9_\.\-]+)@([A-Za-z0-9\.\-]+)$")
            If userOnDomain.Success Then
                Return String.Format("https://{0}/@{1}", userOnDomain.Groups(2).Value, userOnDomain.Groups(1).Value)
            End If

            Dim userOnly As Match = Regex.Match(trimmed, "^@?([A-Za-z0-9_\.\-]+)$")
            If userOnly.Success Then
                Return String.Format("https://{0}/@{1}", GetDefaultHost(), userOnly.Groups(1).Value)
            End If

            If trimmed.StartsWith("/", StringComparison.Ordinal) Then
                Return GetSiteBaseUrl() & trimmed
            End If

            If trimmed.IndexOf("/"c) >= 0 Then
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

        Private Function GetSiteBaseUrl() As String
            Dim host As String = GetDefaultHost()
            Return "https://" & host
        End Function

        Private Function GetDefaultHost() As String
            Dim host As String = DefaultDomain
            Dim settingsData As SiteSettings = TryCast(Settings, SiteSettings)
            If settingsData IsNot Nothing Then
                Dim configured As String = CStr(settingsData.Domain.Value)
                If Not String.IsNullOrWhiteSpace(configured) Then host = configured.Trim()
            End If

            If host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then host = host.Substring(7)
            If host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then host = host.Substring(8)
            Return host.Trim().Trim("/"c)
        End Function

        Private Shared Function TryExtractStatusId(ByVal path As String, ByRef statusId As String) As Boolean
            statusId = String.Empty
            If String.IsNullOrWhiteSpace(path) Then Return False

            Dim m As Match = Regex.Match(path, "^/@[^/]+/(\d+)/?$", RegexOptions.IgnoreCase)
            If Not m.Success Then m = Regex.Match(path, "^/web/statuses/(\d+)/?$", RegexOptions.IgnoreCase)
            If Not m.Success Then m = Regex.Match(path, "^/users/[^/]+/statuses/(\d+)/?$", RegexOptions.IgnoreCase)
            If Not m.Success Then Return False

            statusId = m.Groups(1).Value
            Return Not String.IsNullOrWhiteSpace(statusId)
        End Function

        Private Shared Function TryExtractProfileToken(ByVal path As String, ByRef token As String) As Boolean
            token = String.Empty
            If String.IsNullOrWhiteSpace(path) Then Return False

            Dim m As Match = Regex.Match(path, "^/@([^/]+)/?$", RegexOptions.IgnoreCase)
            If Not m.Success Then m = Regex.Match(path, "^/users/([^/]+)/?$", RegexOptions.IgnoreCase)
            If Not m.Success Then Return False

            token = m.Groups(1).Value
            Return Not String.IsNullOrWhiteSpace(token)
        End Function

        Private Shared Function BuildLookupAcct(ByVal token As String, ByVal host As String) As String
            If String.IsNullOrWhiteSpace(token) Then Return String.Empty

            Dim acct As String = token.Trim().TrimStart("@"c)
            If String.IsNullOrWhiteSpace(acct) Then Return String.Empty

            If acct.IndexOf("@"c) >= 0 Then Return acct
            Return acct & "@" & host
        End Function

        Private Shared Function BuildStatusPageUrl(ByVal host As String, ByVal statusId As String) As String
            If String.IsNullOrWhiteSpace(host) OrElse String.IsNullOrWhiteSpace(statusId) Then Return String.Empty
            Return String.Format("https://{0}/web/statuses/{1}", host, statusId)
        End Function

        Private Shared Function IsDirectMediaPath(ByVal path As String) As Boolean
            If String.IsNullOrWhiteSpace(path) Then Return False
            Return Regex.IsMatch(path, "\.(?:jpg|jpeg|png|gif|webp|bmp|mp4|webm|mov|m4v|mkv|avi|mp3|m4a|ogg|wav|flac|aac)$", RegexOptions.IgnoreCase)
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

        Private Shared Function NormalizeMediaKey(ByVal url As String) As String
            If String.IsNullOrWhiteSpace(url) Then Return String.Empty

            Dim uri As Uri = Nothing
            If Uri.TryCreate(url, UriKind.Absolute, uri) Then
                Return (uri.GetLeftPart(UriPartial.Path)).ToLowerInvariant()
            End If

            Return url.Trim().ToLowerInvariant()
        End Function

        Private Shared Function FirstNotEmpty(ParamArray values() As String) As String
            If values Is Nothing Then Return String.Empty
            For Each value As String In values
                If Not String.IsNullOrWhiteSpace(value) Then Return value
            Next
            Return String.Empty
        End Function

        Private Shared Function StripHtml(ByVal value As String) As String
            If String.IsNullOrWhiteSpace(value) Then Return String.Empty

            Dim noTags As String = Regex.Replace(value, "<[^>]+>", " ")
            noTags = WebUtility.HtmlDecode(noTags)
            noTags = Regex.Replace(noTags, "\s+", " ")
            Return noTags.Trim()
        End Function

        Private Shared Function InferMediaType(ByVal rawType As String, ByVal directUrl As String) As UserMediaTypes
            Select Case If(rawType, String.Empty).ToLowerInvariant()
                Case "image"
                    Return UserMediaTypes.Picture
                Case "video"
                    Return UserMediaTypes.Video
                Case "gifv"
                    Return UserMediaTypes.GIF
                Case "audio"
                    Return UserMediaTypes.Audio
            End Select

            Dim ext As String = String.Empty
            Dim uri As Uri = Nothing
            If Uri.TryCreate(directUrl, UriKind.Absolute, uri) Then ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant()

            Select Case ext
                Case ".jpg", ".jpeg", ".png", ".bmp", ".webp"
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

        Private Shared Function BuildMediaFileName(ByVal directUrl As String,
                                                   ByVal postId As String,
                                                   ByVal mediaId As String,
                                                   ByVal mediaType As UserMediaTypes) As String
            Dim fileName As String = String.Empty

            Dim uri As Uri = Nothing
            If Uri.TryCreate(directUrl, UriKind.Absolute, uri) Then
                fileName = Path.GetFileName(uri.LocalPath)
            End If

            If String.IsNullOrWhiteSpace(fileName) Then
                fileName = String.Format("mastodon_{0}_{1}", postId, mediaId)
            End If

            fileName = SanitizeFileName(fileName)
            If String.IsNullOrWhiteSpace(Path.GetExtension(fileName)) Then
                fileName &= InferExtension(mediaType)
            End If

            Return fileName
        End Function

        Private Shared Function CreateDirectMedia(ByVal directUrl As String, ByVal sourceUrl As String) As IUserMedia
            Dim mediaType As UserMediaTypes = InferMediaType(String.Empty, directUrl)
            Dim postId As String = ExtractIdFromUrl(directUrl)
            Dim fileName As String = BuildMediaFileName(directUrl, If(postId, "media"), "1", mediaType)

            Return New PluginUserMedia With {
                .ContentType = mediaType,
                .URL = directUrl,
                .URL_BASE = If(String.IsNullOrWhiteSpace(sourceUrl), directUrl, sourceUrl),
                .File = fileName,
                .PostID = postId,
                .PostDate = Nothing,
                .DownloadState = UserMediaStates.Unknown
            }
        End Function

        Private Shared Function ExtractIdFromUrl(ByVal url As String) As String
            Dim uri As Uri = Nothing
            If Not Uri.TryCreate(url, UriKind.Absolute, uri) Then Return String.Empty
            Return Path.GetFileNameWithoutExtension(uri.LocalPath)
        End Function

        Private Function ResolveOutputPath() As String
            If Not String.IsNullOrWhiteSpace(DataPath) Then Return DataPath
            Return Path.Combine(Environment.CurrentDirectory, "MastodonDownloads")
        End Function

        Private Sub HandleRequestFailure(ByVal ex As WebException, ByVal context As String)
            Dim status As HttpStatusCode? = GetStatusCode(ex)
            If status.HasValue Then
                Select Case status.Value
                    Case HttpStatusCode.NotFound
                        UserExists = False
                        LogProvider?.Add("Mastodon: target resource was not found.")
                    Case HttpStatusCode.Gone
                        UserSuspended = True
                        LogProvider?.Add("Mastodon: target resource is no longer available.")
                    Case HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized
                        LogProvider?.Add("Mastodon: request denied (authorization or permissions issue).")
                    Case CType(429, HttpStatusCode)
                        LogProvider?.Add("Mastodon: rate limited by remote instance. Try again later.")
                    Case Else
                        LogProvider?.Add(ex, context)
                End Select
            Else
                LogProvider?.Add(ex, context)
            End If
        End Sub

        Private Shared Function GetStatusCode(ByVal ex As WebException) As HttpStatusCode?
            If ex Is Nothing Then Return Nothing

            Dim response As HttpWebResponse = TryCast(ex.Response, HttpWebResponse)
            If response Is Nothing Then Return Nothing

            Return response.StatusCode
        End Function

        Private Shared Function CreateSerializer() As JavaScriptSerializer
            Return New JavaScriptSerializer With {
                .MaxJsonLength = Integer.MaxValue
            }
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

        Private Enum TargetKind
            None = 0
            Profile = 1
            Status = 2
            DirectMedia = 3
        End Enum

        Private Structure ResolvedTarget
            Public Kind As TargetKind
            Public AbsoluteUrl As String
            Public Host As String
            Public AccountLookup As String
            Public StatusID As String
        End Structure

        Private NotInheritable Class MastodonAccount
            Public Property id As String
            Public Property username As String
            Public Property acct As String
            Public Property display_name As String
            Public Property note As String
            Public Property url As String
        End Class

        Private NotInheritable Class MastodonStatus
            Public Property id As String
            Public Property created_at As String
            Public Property url As String
            Public Property content As String
            Public Property media_attachments As List(Of MastodonMediaAttachment)
            Public Property reblog As MastodonStatus
        End Class

        Private NotInheritable Class MastodonMediaAttachment
            Public Property id As String
            Public Property type As String
            Public Property url As String
            Public Property preview_url As String
            Public Property remote_url As String
            Public Property description As String
        End Class

    End Class
End Namespace
