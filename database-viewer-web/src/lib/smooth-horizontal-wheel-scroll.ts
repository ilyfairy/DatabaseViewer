'use strict';

export type DetachSmoothHorizontalWheelScroll = () => void;

const edgeSnapDistance = 8;

function getActualMaxScrollLeft(element: HTMLElement): number {
  const previousScrollLeft = element.scrollLeft;
  element.scrollLeft = Number.MAX_SAFE_INTEGER;
  const actualMaxScrollLeft = element.scrollLeft;
  element.scrollLeft = previousScrollLeft;
  return actualMaxScrollLeft;
}

function normalizeScrollTarget(scrollLeft: number, maxScrollLeft: number): number {
  const clampedScrollLeft = Math.min(Math.max(scrollLeft, 0), maxScrollLeft);
  if (clampedScrollLeft <= edgeSnapDistance) {
    return 0;
  }

  if (maxScrollLeft - clampedScrollLeft <= edgeSnapDistance) {
    return maxScrollLeft;
  }

  return Math.round(clampedScrollLeft);
}

export function attachSmoothHorizontalWheelScroll(element: HTMLElement): DetachSmoothHorizontalWheelScroll {
  let animationFrameId: number | null = null;
  let targetScrollLeft = normalizeScrollTarget(element.scrollLeft, getActualMaxScrollLeft(element));

  const syncTargetToCurrentScroll = (): void => {
    targetScrollLeft = normalizeScrollTarget(element.scrollLeft, getActualMaxScrollLeft(element));
  };

  const animate = (): void => {
    const maxScrollLeft = getActualMaxScrollLeft(element);
    targetScrollLeft = normalizeScrollTarget(targetScrollLeft, maxScrollLeft);

    const currentScrollLeft = element.scrollLeft;
    const delta = targetScrollLeft - currentScrollLeft;
    if (Math.abs(delta) < 0.5) {
      element.scrollLeft = targetScrollLeft;
      animationFrameId = null;
      return;
    }

    element.scrollLeft = currentScrollLeft + delta * 0.24;
    if (Math.abs(element.scrollLeft - currentScrollLeft) < 0.01) {
      element.scrollLeft = targetScrollLeft;
      animationFrameId = null;
      return;
    }

    animationFrameId = window.requestAnimationFrame(animate);
  };

  const handleWheel = (event: WheelEvent): void => {
    if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) {
      return;
    }

    const maxScrollLeft = getActualMaxScrollLeft(element);
    if (maxScrollLeft <= 0) {
      return;
    }

    // 如果滚动位置被外部逻辑改过（例如激活新 tab 后自动滚动），
    // 下一次滚轮必须从当前真实位置继续，而不是沿用旧目标值。
    if (animationFrameId === null) {
      syncTargetToCurrentScroll();
    }

    targetScrollLeft = normalizeScrollTarget(targetScrollLeft + event.deltaY, maxScrollLeft);
    event.preventDefault();

    if (animationFrameId === null) {
      animationFrameId = window.requestAnimationFrame(animate);
    }
  };

  const handleScroll = (): void => {
    if (animationFrameId !== null) {
      return;
    }

    syncTargetToCurrentScroll();
  };

  element.addEventListener('wheel', handleWheel, { passive: false });
  element.addEventListener('scroll', handleScroll, { passive: true });

  return () => {
    element.removeEventListener('wheel', handleWheel);
    element.removeEventListener('scroll', handleScroll);

    if (animationFrameId !== null) {
      window.cancelAnimationFrame(animationFrameId);
    }
  };
}