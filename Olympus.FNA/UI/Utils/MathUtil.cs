using System;

namespace OlympUI {
    public static class MathUtil {
        /// <summary>
        /// Does a simple binary search.
        /// </summary>
        /// <param name="min">The lowest value.</param>
        /// <param name="max">The highest value.</param>
        /// <param name="comparer">The predicate to determinate where the target value lies. True represents the target value is above current</param>
        /// <returns>An index between `max` and `min` that satisfies the current comparer</returns>
        public static int BinarySearch(int min, int max, Predicate<int> comparer) {
            int mid = min;
            if (max < min) {
                (max, min) = (min, max);
            }
            while (max - min >= 1) {
                mid = (int) Math.Floor((max + min) / 2D);
                if (comparer.Invoke(mid)) {
                    mid++;
                    min = mid;
                } else {
                    max = mid;
                }
            }

            return mid;
        }
    }
}