using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BezierSpline
{
    private Vector3[] controlPoints;

    public BezierSpline(Vector3[] points)
    {
        controlPoints = points;
    }

    // public Vector3 GetPoint(float t)
    // {
        // int numSegments = controlPoints.Length / 3;
        // int currentIndex = Mathf.FloorToInt(t * numSegments);
        // float segmentT = t * numSegments - currentIndex;

        // Vector3 p0 = GetControlPoint(currentIndex * 3);
        // Vector3 p1 = GetControlPoint(currentIndex * 3 + 1);
        // Vector3 p2 = GetControlPoint(currentIndex * 3 + 2);
        // Vector3 p3 = GetControlPoint(currentIndex * 3 + 3);

        // return Bezier.GetPoint(p0, p1, p2, p3, segmentT);
    // }

    public Vector3 GetControlPoint(int index)
    {
        return controlPoints[index];
    }
}
