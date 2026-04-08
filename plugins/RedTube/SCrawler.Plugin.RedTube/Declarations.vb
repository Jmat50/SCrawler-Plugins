Imports SCrawler.Plugin

Namespace RedTubePlugin
    Friend Module Declarations
        Friend Const PluginKey As String = "RedTube"
        Friend Const SiteDisplayName As String = "RedTube"
        Friend Const DefaultDomain As String = "www.redtube.com"
        Friend Const DefaultListingUrlPattern As String = "https://www.redtube.com/{0}"
        Friend Const Mp4MediaPathToken As String = "/media/mp4?s="
        Friend Const MaxListingPages As Integer = 250

        Friend Function IsSupportedListingPath(ByVal absolutePath As String) As Boolean
            If String.IsNullOrWhiteSpace(absolutePath) Then Return False

            Dim segments() As String = absolutePath.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If segments.Length = 0 Then Return False
            If segments.Length > 3 Then Return False

            For Each segment As String In segments
                If String.IsNullOrWhiteSpace(segment) Then Return False
                If segment.IndexOf("."c) >= 0 Then Return False
            Next

            Dim head As String = segments(0).Trim().ToLowerInvariant()
            Select Case head
                Case "user", "users", "register", "login", "recently_viewed", "watch", "embed", "media"
                    Return False
            End Select

            If segments.Length = 3 Then
                Return head = "straight" AndAlso
                       segments(1).Equals("playlists", StringComparison.OrdinalIgnoreCase) AndAlso
                       Not String.IsNullOrWhiteSpace(segments(2))
            End If

            Return True
        End Function
    End Module
End Namespace
