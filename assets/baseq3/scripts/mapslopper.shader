// MapSlopper default shaders. These define visible "no texture needed"
// surfaces using q3map2's built-in $whiteimage so projects compile and look
// reasonable without depending on baseq3 .pk3 art assets.
//
// Drop this file at <fs_basepath>/baseq3/scripts/mapslopper.shader and add
// "mapslopper" as a line in baseq3/scripts/shaderlist.txt (or simply rely
// on q3map2's "no shaderlist.txt found: loading all shaders" fallback).

textures/random/floor
{
    qer_editorimage textures/random/floor.tga
    surfaceparm nomarks
    {
        map $whiteimage
        rgbGen const ( 0.55 0.55 0.62 )
    }
    {
        map $lightmap
        blendFunc filter
        rgbGen identity
    }
}

textures/random/wall
{
    qer_editorimage textures/random/wall.tga
    {
        map $whiteimage
        rgbGen const ( 0.45 0.42 0.40 )
    }
    {
        map $lightmap
        blendFunc filter
        rgbGen identity
    }
}

textures/random/ceiling
{
    qer_editorimage textures/random/ceiling.tga
    {
        map $whiteimage
        rgbGen const ( 0.32 0.30 0.34 )
    }
    {
        map $lightmap
        blendFunc filter
        rgbGen identity
    }
}
