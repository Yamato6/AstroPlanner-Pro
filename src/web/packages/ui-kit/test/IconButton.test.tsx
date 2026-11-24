import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { Moon, Play } from 'lucide-react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { IconButton, ToggleIconButton } from '../src';

afterEach(cleanup);

describe('IconButton', () => {
  it('is named by its label and presses', () => {
    const onPress = vi.fn();
    render(<IconButton label="Play" icon={Play} onPress={onPress} />);

    fireEvent.click(screen.getByRole('button', { name: 'Play' }));

    expect(onPress).toHaveBeenCalledOnce();
  });

  it('does nothing while disabled', () => {
    const onPress = vi.fn();
    render(<IconButton label="Play" icon={Play} onPress={onPress} isDisabled />);

    const button = screen.getByRole('button', { name: 'Play' });
    fireEvent.click(button);

    expect(button).toHaveProperty('disabled', true);
    expect(onPress).not.toHaveBeenCalled();
  });
});

describe('ToggleIconButton', () => {
  it('announces its state and reports the new one', () => {
    const onChange = vi.fn();
    render(<ToggleIconButton label="Night vision" icon={Moon} isSelected={false} onChange={onChange} />);

    const button = screen.getByRole('button', { name: 'Night vision' });
    expect(button.getAttribute('aria-pressed')).toBe('false');
    fireEvent.click(button);

    expect(onChange).toHaveBeenCalledWith(true);
  });
});
