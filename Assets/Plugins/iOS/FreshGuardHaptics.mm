#import <UIKit/UIKit.h>
#import <dispatch/dispatch.h>

extern "C" void FreshGuardPlayLightImpact(float intensity)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 10.0, *))
        {
            static UIImpactFeedbackGenerator *generator = nil;
            if (generator == nil)
                generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];

            [generator prepare];
            if (@available(iOS 13.0, *))
                [generator impactOccurredWithIntensity:intensity];
            else
                [generator impactOccurred];
        }
    });
}
