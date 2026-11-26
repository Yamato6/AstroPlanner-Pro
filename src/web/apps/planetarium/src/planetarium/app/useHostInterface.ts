// The interface state the host keeps (ui.get / ui.changed): night vision and reduce transparency
// become attributes of <html>, which the tokens read; nothing re-renders. The sky's options (drag
// inertia, atmosphere quality, light-pollution colour, twinkling) and the survey (survey.get /
// survey.changed) go to the engine.
import { skyControl } from '@astroplanner/sky-engine';
import { useEffect } from 'react';
import { useSnapshot } from './hostQueries';

export function useHostInterface(): void {
  const { data: ui } = useSnapshot('ui.get');
  const { data: survey } = useSnapshot('survey.get');

  useEffect(() => {
    if (!survey) {
      return;
    }
    const chosen = survey.surveys.find((s) => s.id === survey.surveyId);
    skyControl.setSurvey(
      chosen
        ? {
            id: chosen.id,
            baseUrl: survey.tilesBaseUrl,
            maxOrder: chosen.maxOrder,
            tileWidth: chosen.tileWidth,
            credit: chosen.credit,
          }
        : null,
    );
    skyControl.setSurveyMode(survey.mode);
  }, [survey]);

  useEffect(() => {
    if (!ui) {
      return;
    }
    const root = document.documentElement;
    root.dataset.theme = ui.theme;
    if (ui.reduceTransparency) {
      root.dataset.transparency = 'reduced';
    } else {
      delete root.dataset.transparency;
    }
    skyControl.setInertia(ui.skyInertia);
    skyControl.setQuality(ui.skyQuality);
    skyControl.setLightPollutionTint(ui.lightPollutionTint);
    skyControl.setScintillation(ui.scintillation);
  }, [ui]);
}
