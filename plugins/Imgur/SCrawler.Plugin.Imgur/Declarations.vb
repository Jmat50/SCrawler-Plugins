Imports SCrawler.Plugin

Namespace ImgurPlugin
    Friend Module Declarations
        Friend Const PluginKey As String = "Imgur"
        Friend Const SiteDisplayName As String = "Imgur (Fixed)"
        Friend Const DefaultDomain As String = "imgur.com"
        Friend Const DefaultListingUrlPattern As String = "https://imgur.com/{0}"
        Friend Const FallbackMirrorPrefix As String = "https://r.jina.ai/http://"
        Friend Const MaxListingPages As Integer = 250
    End Module
End Namespace
