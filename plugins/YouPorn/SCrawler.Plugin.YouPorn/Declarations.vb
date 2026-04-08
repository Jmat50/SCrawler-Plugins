Imports SCrawler.Plugin

Namespace YouPornPlugin
    Friend Module Declarations
        Friend Const PluginKey As String = "YouPorn"
        Friend Const SiteDisplayName As String = "YouPorn"
        Friend Const DefaultDomain As String = "www.youporn.com"
        Friend Const DefaultListingUrlPattern As String = "https://www.youporn.com/channel/{0}/"
        Friend Const WatchPathPrefix As String = "/watch/"
        Friend Const Mp4MediaPathToken As String = "/media/mp4/?s="
        Friend Const MaxListingPages As Integer = 250

        Friend Function IsSupportedListingPath(ByVal absolutePath As String) As Boolean
            If String.IsNullOrWhiteSpace(absolutePath) Then Return False

            Dim segments() As String = absolutePath.Split({"/"c}, StringSplitOptions.RemoveEmptyEntries)
            If segments.Length = 0 Then Return False

            Dim head As String = segments(0).Trim().ToLowerInvariant()
            If String.IsNullOrWhiteSpace(head) Then Return False

            Select Case segments.Length
                Case 1
                    Select Case head
                        Case "recommended", "top_rated", "most_viewed", "most_favorited"
                            Return True
                    End Select
                Case 2
                    If String.IsNullOrWhiteSpace(segments(1)) Then Return False

                    Select Case head
                        Case "browse", "category", "pornstar", "channel", "porntags"
                            Return True
                    End Select
            End Select

            Return False
        End Function
    End Module
End Namespace
